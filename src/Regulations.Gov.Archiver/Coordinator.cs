using Akka.Actor;
using Akka.Event;
using Regulations.Gov.Client;
using Akka.DI.Core;
using Nest;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Regulations.Gov.Archiver
{
    public class Coordinator : ReceiveActor
    {
        public Coordinator(IStorageService storageService, ElasticClient elasticClient)
        {
            Context.GetLogger().Info("Running coordinator");
            var requester = Context.ActorOf(Context.DI().Props<Requester>());

            string UnprocessedIndices = "unprocessed";
            if (elasticClient.IndexExists(UnprocessedIndices).Exists)
            {
                var indexConfig = new IndexState
                {
                    Settings = new IndexSettings
                    {
                        NumberOfReplicas = 2,
                        NumberOfShards = 10,
                    },
                };

                var createIndexResult = elasticClient.CreateIndex(UnprocessedIndices, c => c
                    .InitializeUsing(indexConfig));
                if (!createIndexResult.IsValid)
                {
                    throw new Exception(createIndexResult.DebugInformation);
                }
            }

            requester.Tell("init");

            ReceiveAsync<IEnumerable<Persistable<DocumentStub>>>(async documents =>
            {
                var result = await elasticClient.IndexManyAsync(documents, UnprocessedIndices);
                if (result.Errors)
                {
                    Context.GetLogger().Error(result.OriginalException, result.DebugInformation);
                    foreach (var item in result.ItemsWithErrors)
                    {
                        Context.GetLogger().Info($"Could not index {item.Id} {item.Error.ToString()}");
                    }
                }
            });
        }
    }
}
