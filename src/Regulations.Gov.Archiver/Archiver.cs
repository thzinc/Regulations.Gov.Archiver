using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;
using ExpressMapper.Extensions;
using Regulations.Gov.Client;
using RestEase;

namespace Regulations.Gov.Archiver
{

    public class Archiver : ReceiveActor
    {
        public Archiver(Uri elasticSearchUrl, string apiKey, string downloadPath)
        {
            var indexer = Context.ActorOf(Props.Create(() => new Indexer(elasticSearchUrl, downloadPath)));
            var client = Context.ActorOf(Props.Create(() => new RegulationGovClient(apiKey)));
            Become(() => Initializing(indexer, client));
        }

        private void Initializing(IActorRef indexer, IActorRef client)
        {
            Receive<string>(_ =>
            {
                indexer.Tell(new GetMaxIndexByPostedDate());
            });

            Receive<MaxIndexByPostedDate>(max =>
            {
                Become(() => Querying(indexer, client, max.Value));
            });

            Self.Tell("bump");
        }

        private void Querying(IActorRef indexer, IActorRef client, int startingPageOffset)
        {
            Receive<int>(pageOffset =>
            {
                client.Tell(new DocumentsQuery
                {
                    SortBy = SortFields.PostedDate,
                    SortOrder = SortOrderType.Ascending,
                    ResultsPerPage = 1000,
                    PageOffset = pageOffset,
                });
            });

            Receive<DocumentsQueryResult>(message =>
            {
                var logger = Context.GetLogger();
                var pageOffset = message.Query.PageOffset ?? 0;
                if (message.Result.Documents == null || message.Result.Documents.Count == 0)
                {
                    logger.Info("Reached the end. Waiting a bit to get more.");
                    Context.System.Scheduler.ScheduleTellOnce(RegulationGovClient.RestPeriod, Self, pageOffset, Self);
                    return;
                }

                logger.Info($"Got {message.Result.Documents.Count} documents, {pageOffset}/{message.Result.TotalNumRecords} total available");

                var nextPageOffset = pageOffset + message.Result.Documents.Count;
                Self.Tell(nextPageOffset);

                Self.Tell(message.Result.Documents
                    .Select((d, i) => d.Map(new Document
                    {
                        IndexByPostedDate = pageOffset + i,
                    }))
                    .ToList());
            });

            Receive<List<Document>>(documents =>
            {
                indexer.Tell(documents);
                documents
                    .SelectMany(d => Enumerable.Range(1, d.AttachmentCount)
                        .Select(an => new GetAttachment
                        {
                            Document = d,
                            AttachmentNumber = an,
                        }))
                    .ForEach(client.Tell);
                documents
                    .Where(d => d.DocumentType != "Public Submission")
                    .Select(d => new GetDownload
                    {
                        Document = d,
                    })
                    .ForEach(client.Tell);
            });

            Receive<Document>(download =>
            {
                indexer.Tell(download);
            });

            Self.Tell(startingPageOffset);
        }
    }
}
