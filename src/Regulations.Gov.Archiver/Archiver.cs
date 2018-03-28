using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Regulations.Gov.Client;
using RestEase;

namespace Regulations.Gov.Archiver
{
    public class Archiver : ReceiveActor
    {
        public Archiver(System.Uri elasticSearchUrl, string apiKey)
        {
            var apiClient = new RegulationsGovClient(apiKey);
            var indexManager = Context.ActorOf(Props.Create(() => new RegulationIndexManager(elasticSearchUrl)));
            Become(() => WaitingForLastPostedDate(indexManager, apiClient));
        }

        private void WaitingForLastPostedDate(IActorRef indexManager, RegulationsGovClient apiClient)
        {
            Receive<RegulationIndexManager.LastPostedDate>(lastPostedDate =>
            {
                Become(() => Querying(lastPostedDate.PostedDate, indexManager, apiClient));
            });

            indexManager.Tell(new RegulationIndexManager.GetLastPostedDate());
        }

        private void Querying(DateTimeOffset? postedStartDate, IActorRef indexManager, RegulationsGovClient apiClient)
        {
            ReceiveAsync<int>(async pageOffset =>
            {
                var logger = Context.GetLogger();
                try
                {
                    var query = new DocumentsQuery
                    {
                        SortBy = SortFields.PostedDate,
                        SortOrder = SortOrderType.Ascending,
                        ResultsPerPage = 1000,
                        PageOffset = pageOffset,
                    };
                    // if (postedStartDate.HasValue)
                    // {
                    //     query["pd"] = new[] { $"{postedStartDate:MM/dd/yy}-" };
                    // }

                    logger.Info(string.Join("; ", query.Select(x => $"{x.Key} = {string.Join(", ", x.Value)}")));

                    var result = await apiClient.GetDocuments(query);
                    logger.Info($"Got {result.Documents.Count} documents, {pageOffset}/{result.TotalNumRecords} total available");

                    indexManager.Tell(result.Documents);
                    Self.Tell(pageOffset + result.Documents.Count);
                }
                catch (ApiException ae)
                {
                    logger.Error(ae, "Caught API exception; waiting a bit to send resend request");
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMinutes(1), Self, pageOffset, Self);
                }
            });

            Self.Tell(0);
        }
    }
}
