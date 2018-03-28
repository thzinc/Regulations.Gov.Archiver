using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Nest;
using Regulations.Gov.Client;

namespace Regulations.Gov.Archiver
{
    public class RegulationIndexManager : ReceiveActor
    {
        public class GetLastPostedDate { }
        public class LastPostedDate
        {
            public DateTimeOffset? PostedDate { get; set; }
        }
        private const string Indices = "regulations2";

        public RegulationIndexManager(Uri elasticSearchUrl)
        {
            var config = new ConnectionSettings(elasticSearchUrl)
                .DefaultIndex(Indices)
                .DefaultMappingFor<DocumentStub>(m => m
                    .IdProperty(x => x.DocumentId));
            var esClient = new ElasticClient(config);
            var indexConfig = new IndexState
            {
                Settings = new IndexSettings
                {
                    NumberOfReplicas = 1,
                    NumberOfShards = 5,
                },
            };

            var response = esClient.CreateIndex(Indices, c => c
                .InitializeUsing(indexConfig)
                .Mappings(m => m
                    .Map<DocumentStub>(mp => mp
                        .AutoMap())));

            Become(() => Ready(esClient));
        }

        private void Ready(ElasticClient esClient)
        {
            ReceiveAsync<GetLastPostedDate>(async message =>
            {
                var result = await esClient.SearchAsync<DocumentStub>(s => s
                    .Aggregations(a => a
                        .Max("last_posted_date", x => x
                            .Field(p => p.PostedDate))));
                var lastPostedMs = result.Aggregations.Max("last_posted_date")?.Value;
                var lastPostedDate = lastPostedMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)lastPostedMs.Value)
                    : (DateTimeOffset?)null;

                Sender.Tell(new LastPostedDate
                {
                    PostedDate = lastPostedDate,
                });
            });

            ReceiveAsync<ICollection<DocumentStub>>(async documents =>
            {
                var logger = Context.GetLogger();
                logger.Info($"Indexing {documents.Count} documents");

                var result = await esClient.IndexManyAsync(documents, Indices);
                if (result.Errors)
                {
                    logger.Error("Errors received!");
                    foreach (var item in result.ItemsWithErrors)
                    {
                        logger.Debug($"{item.Id}: {item.Error.Reason} {item.Error.RootCause}");
                    }
                }
            });
        }
    }
}
