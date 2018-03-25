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
        private const string Indices = "regulations";

        public RegulationIndexManager(Uri elasticSearchUrl)
        {
            var config = new ConnectionSettings(elasticSearchUrl);
            var esClient = new ElasticClient(config);
            var indexConfig = new IndexState
            {
                Settings = new IndexSettings
                {
                    NumberOfReplicas = 1,
                    NumberOfShards = 2,
                },
            };

            if (!esClient.IndexExists(Indices).Exists)
            {
                esClient.CreateIndex(Indices, c => c
                    .InitializeUsing(indexConfig)
                    .Mappings(m => m
                        .Map<DocumentStub>(mp => mp.AutoMap())));
            }

            Become(() => WaitingForDocuments(esClient));
        }

        private void WaitingForDocuments(ElasticClient esClient)
        {
            ReceiveAsync<IEnumerable<DocumentStub>>(async documents =>
            {
                var result = await esClient.IndexManyAsync(documents, Indices);
                var logger = Context.GetLogger();
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
