using System;
using System.Linq;
using Akka.Actor;
using Akka.DI.Core;
using Akka.Event;
using Google.Apis.Drive.v3;
using GoogleClient;

namespace Regulations.Gov.Ingester.Actors
{
    public class Discoverer : ReceiveActor
    {
        private readonly DriveService _service;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex => Directive.Restart);
        }

        public Discoverer(DriveService service)
        {
            _service = service;

            Become(() => Downloading());
        }

        private void Downloading()
        {
            var downloaders = Enumerable.Range(0, 2)
                .Select(i => Context.ActorOf(Context.DI().Props<Downloader>(), $"{nameof(Downloader)}-{i:00}"))
                .ToArray();
            ReceiveAsync<string>(async next =>
            {
                Context.GetLogger().Info($"Getting page of documents");
                var list = _service.Files.List();
                list.Q = "appProperties has { key='Collection' and value='Regulations.Gov' } and not mimeType = 'application/vnd.google-apps.folder'";
                list.PageToken = next;
                list.PageSize = 1000;
                list.Fields = "incompleteSearch,nextPageToken,files(id,name,mimeType,createdTime,modifiedTime,originalFilename,properties)";
                var response = await list.ExecuteAsync();
                foreach (var (file, i) in response.Files.Select((f, i) => (f, i)))
                {
                    var downloader = downloaders[i % downloaders.Length];
                    downloader.Tell(new GetFile
                    {
                        Target = Context.Parent,
                        File = file,
                        DocumentId = file.Properties["DocumentId"],
                    });
                }
                if (!string.IsNullOrEmpty(response.NextPageToken))
                {
                    Self.Tell(response.NextPageToken);
                }
                else
                {
                    Context.GetLogger().Info("Reached the end of the list; sleeping for a bit");
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMinutes(5), Self, "", Self);
                }
            });

            Self.Tell("");
        }
    }
}
