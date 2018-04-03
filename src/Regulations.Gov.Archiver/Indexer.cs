using System;
using System.Collections.Generic;
using System.IO;
using Akka.Actor;
using Akka.Event;
using MimeTypes;
using Nest;
using Regulations.Gov.Client;

namespace Regulations.Gov.Archiver
{
    public class Indexer : ReceiveActor
    {
        private const string Indices = "regulations3";

        public Indexer(Uri elasticSearchUrl, string downloadPath)
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

            var createIndexResponse = esClient.CreateIndex(Indices, c => c
                .InitializeUsing(indexConfig)
                .Settings(s => s
                    .Analysis(a => a
                        .Analyzers(an => an
                            .Custom("comment", ca => ca
                                .Tokenizer("standard")
                                .Filters("lowercase", "porter_stem"))
                            .Fingerprint("fingerprint", f => f
                                .StopWords("_english_")
                                .MaxOutputSize(10240))))));
            if (!createIndexResponse.IsValid)
            {
                Context.GetLogger().Info($"Did not re-create index {Indices}");
            }

            var documentMappingResponse = esClient.Map<Document>(m => m
                .AutoMap()
                .Properties(p => p
                    .Text(t => t
                        .Name(d => d.Attachment.Content)
                        .CopyTo(c => c.Field(d => d.CommentText)))
                    .Text(t => t
                        .Name(d => d.CommentText)
                        .CopyTo(c => c.Field(d => d.Fingerprint))
                        .Analyzer("fingerprint"))
                    .Text(t => t
                        .Name(d => d.CommentText)
                        .Analyzer("comment"))));
            if (!documentMappingResponse.IsValid) throw new Exception($"Could not apply document mapping to {Indices}: {documentMappingResponse.ServerError}");

            var putPipelineResponse = esClient.PutPipeline("download", p => p
                .Processors(ps => ps
                    .Attachment<Document>(a => a
                        .Field(d => d.Data)
                        .IndexedCharacters(-1)
                        .IgnoreMissing(true)
                        .TargetField(d => d.Attachment))
                    .Remove<Document>(r => r
                        .Field(d => d.Data))));
            if (!putPipelineResponse.IsValid) throw new Exception($"Could not create pipeline: {putPipelineResponse.ServerError}");

            Become(() => Ready(esClient, downloadPath));
        }

        private void Ready(ElasticClient esClient, string downloadPath)
        {
            ReceiveAsync<GetMaxIndexByPostedDate>(async request =>
            {
                var result = await esClient.SearchAsync<Document>(s => s
                    .Aggregations(a => a
                        .Max("index_by_posted_date", m => m
                            .Field(d => d.IndexByPostedDate))));
                var maxIndexByPostedDate = result.Aggregations.Max("index_by_posted_date")?.Value ?? 0;
                Sender.Tell(new MaxIndexByPostedDate
                {
                    Value = (int)maxIndexByPostedDate,
                });
            });

            ReceiveAsync<ICollection<Document>>(async documents =>
            {
                var logger = Context.GetLogger();
                logger.Info($"Indexing {documents.Count} documents");

                var result = await esClient.BulkAsync(b => b
                    .Index(Indices)
                    .IndexMany(documents));
                if (result.Errors)
                {
                    logger.Error(result.OriginalException, $"Error(s) while indexing documents");
                    foreach (var item in result.ItemsWithErrors)
                    {
                        logger.Info($"{item.Id}: {item.Error.Reason} {item.Error.RootCause}");
                    }
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMinutes(1), Self, documents, Sender);
                }
            });

            ReceiveAsync<Document>(async download =>
            {
                var logger = Context.GetLogger();
                logger.Info($"Indexing download {download.Id}");

                if (!string.IsNullOrEmpty(download.Data))
                {
                    var extension = MimeTypeMap.GetExtension(download.ReportedContentType);
                    var filename = Path.Combine(downloadPath, $"{download.Id}{extension}");
                    await File.WriteAllBytesAsync(filename, Convert.FromBase64String(download.Data));
                }

                var result = await esClient.IndexAsync(download, i => i
                    .Index(Indices)
                    .Pipeline("download"));
                if (!result.IsValid)
                {
                    logger.Error("Errors received!");
                    logger.Info($"{result.ServerError?.Error?.Reason}");
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMinutes(1), Self, download, Sender);
                }
            }, document => document.DocumentIdType != null);
        }
    }
}
