using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Nest;

namespace Regulations.Gov.Ingester.Actors
{
    public class Ingester : ReceiveActor
    {
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex => Directive.Restart);
        }

        public Ingester(ElasticClient client)
        {
            var pipelineResult = client.PutPipeline("json-content-pipeline", p => p
                .Processors(ps => ps
                    .Json<object>(j => j
                        .Field("json")
                        .TargetField("content"))
                    .Remove<object>(r => r
                        .Field("json"))));

            var indices = new HashSet<string>();
            async Task GetOrAddIndex(string index, Func<Task> add)
            {
                if (indices.Contains(index)) return;

                var exists = await client.IndexExistsAsync(index);
                if (exists.IsValid && exists.Exists)
                {
                    indices.Add(index);
                    return;
                }

                await add();
                indices.Add(index);
            }
            ReceiveAsync<DocumentReference>(async reference =>
            {
                var index = $"unprocessed-{GetIndexYear(reference.DocumentId)}-references";
                var logger = Context.GetLogger();
                logger.Info($"Indexing {reference.DocumentId} in {index}");
                await GetOrAddIndex(index, async () =>
                {
                    var result = await client.CreateIndexAsync(index, c => c
                        .Mappings(ms => ms
                            .Map<DocumentReference>(m => m.AutoMap())));
                    if (!result.IsValid)
                    {
                        logger.Error($"Error while creating index {index}: {result?.ServerError?.Error?.Reason} {result?.ServerError?.Error?.RootCause}");
                        result.TryGetServerErrorReason(out var reason);
                        throw new Exception(reason ?? "Blew up for some reason");
                    }
                });
                var response = await client.IndexAsync(reference, i => i
                    .Index(index)
                    .Id(reference.Id)
                    .Pipeline("json-content-pipeline"));

                if (response.IsValid)
                {
                    logger.Info($"Indexed reference {reference.DocumentId}");
                }
                else
                {
                    logger.Error($"Error indexing reference {reference.DocumentId}: {response.ServerError}");
                    Self.Tell(reference);
                }
            });

            ReceiveAny(message => Context.GetLogger().Warning($"Received message but didn't handle it {message}"));
        }

        public string GetIndexYear(string documentId)
        {
            return (documentId ?? "")
                .Split('-')
                .Select(s => int.TryParse(s, out var i) ? i : 0)
                .Where(i => i >= 1800 && i <= DateTimeOffset.Now.AddYears(2).Year)
                .Select(i => $"{i:0000}")
                .FirstOrDefault() ?? "0000";
        }
    }
}
