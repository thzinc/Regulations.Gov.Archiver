using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Google.Apis.Download;
using Google.Apis.Drive.v3;

namespace Regulations.Gov.Ingester.Actors
{
    public class Downloader : ReceiveActor, IWithUnboundedStash
    {
        private readonly DriveService _service;

        public IStash Stash { get; set; }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex => Directive.Restart);
        }

        public Downloader(DriveService service)
        {
            _service = service;

            // ReceiveAsync<GetDocument>(async request =>
            // {
            //     Context.GetLogger().Info($"Downloading files for {request.DocumentId}");

            //     var list = _service.Files.List();
            //     list.Q = $"'{request.FileId}' in parents";
            //     list.PageSize = 1000;
            //     list.Fields = "incompleteSearch,nextPageToken,files(id,name,mimeType,createdTime,modifiedTime,originalFilename)";
            //     var incompleteSearch = false;
            //     do
            //     {
            //         var response = await list.ExecuteAsync();
            //         foreach (var file in response.Files)
            //         {
            //             Self.Tell(new GetFile
            //             {
            //                 Target = Sender,
            //                 File = file,
            //                 DocumentId = request.DocumentId,
            //             });
            //         }
            //         list = _service.Files.List();
            //         list.PageToken = response.NextPageToken;
            //         incompleteSearch = !string.IsNullOrEmpty(response.NextPageToken);
            //     } while (incompleteSearch);
            // });

            Become(() => Waiting());
        }

        private void Waiting()
        {
            ReceiveAsync<GetFile>(async request =>
            {
                var contentRequest = _service.Files.Get(request.File.Id);
                var stream = new System.IO.MemoryStream();
                var self = Self;
                var logger = Context.GetLogger();
                contentRequest.MediaDownloader.ProgressChanged += (progress) =>
                {
                    switch (progress.Status)
                    {
                        case DownloadStatus.Completed:
                            var bytes = stream.ToArray();
                            stream.Dispose();
                            switch (request.File.Name)
                            {
                                case "reference.json":
                                    request.Target.Tell(new DocumentReference
                                    {
                                        DocumentId = request.DocumentId,
                                        Content = bytes,
                                        MimeType = request.File.MimeType,
                                        Source = "Google Drive",
                                        SourceId = request.File.Id,
                                        CreatedTime = request.File.CreatedTime,
                                        ModifiedTime = request.File.ModifiedTime,
                                    });
                                    break;
                                case "document.json":
                                    request.Target.Tell(new Document
                                    {
                                        DocumentId = request.DocumentId,
                                        Content = bytes,
                                        MimeType = request.File.MimeType,
                                        Source = "Google Drive",
                                        SourceId = request.File.Id,
                                        CreatedTime = request.File.CreatedTime,
                                        ModifiedTime = request.File.ModifiedTime,
                                    });
                                    break;
                                default:
                                    request.Target.Tell(new Other
                                    {
                                        Id = request.File.Name,
                                        OriginalFilename = request.File.OriginalFilename,
                                        DocumentId = request.DocumentId,
                                        Content = bytes,
                                        MimeType = request.File.MimeType,
                                        Source = "Google Drive",
                                        SourceId = request.File.Id,
                                        CreatedTime = request.File.CreatedTime,
                                        ModifiedTime = request.File.ModifiedTime,
                                    });
                                    break;
                            }
                            self.Tell(new DownloadCompleted());
                            break;
                        case DownloadStatus.Failed:
                            logger.Error($"Failed to download file {(request.File.Properties["DocumentId"])} {request.File.Name} ({request.File.Id}, {request.File.MimeType})");
                            self.Tell(new DownloadFailed
                            {
                                OriginalRequest = request,
                            });
                            break;
                    }
                };

                await contentRequest.DownloadAsync(stream);
                Become(() => Downloading());
            });
        }

        private void Downloading()
        {
            Receive<DownloadCompleted>(_ =>
            {
                Stash.UnstashAll();
                Become(() => Waiting());
            });

            Receive<DownloadFailed>(result =>
            {
                Stash.UnstashAll();
                Self.Tell(result.OriginalRequest); 
                Become(() => Waiting());
            });

            ReceiveAny(_ => Stash.Stash());
        }

        public class DownloadCompleted { }
        public class DownloadFailed
        {
            public GetFile OriginalRequest { get; set; }
        }
    }
}
