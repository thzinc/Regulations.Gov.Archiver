using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using GoogleClient;
using Google.Apis.Drive.v3;
using Newtonsoft.Json;
using Regulations.Gov.Downloader.Clients;

namespace Regulations.Gov.Downloader.Actors
{
    public class Persister : ReceiveActor
    {
        private readonly DriveService _service;
        private readonly Dictionary<string, string> _folderIdsByName = new Dictionary<string, string>();

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: null,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex => Directive.Restart);
        }

        public Persister(DriveService service, GoogleSettings settings)
        {
            _service = service;
            Become(() => Initializing(settings.Path));
        }

        private void Initializing(string path)
        {
            ReceiveAsync<string>(async _ =>
            {
                var rootFolder = await CreateFolder("root", path);
                Become(() => Persisting(rootFolder));
            });

            Self.Tell("bump");
        }

        private void Persisting(string rootFolder)
        {
            ReceiveAsync<PersistFile>(async download =>
            {
                var folder = await CreateFolder(rootFolder, download.DocumentId);
                var sanitizedOriginalFileName = Regex.Replace(download.OriginalFileName, @"\W", "-");
                var extension = Path.GetExtension(download.OriginalFileName);
                var file = new Google.Apis.Drive.v3.Data.File
                {
                    Name = $"{download.Tag}{extension}",
                    Parents = new[] { folder },
                    MimeType = download.ContentType,
                    OriginalFilename = download.OriginalFileName,
                    AppProperties = new Dictionary<string, string>
                    {
                        { "Collection", "Regulations.Gov" },
                    },
                    Properties = new Dictionary<string, string>
                    {
                        { "Source", $"{typeof(Persister).FullName}" },
                        { "DocumentId", download.DocumentId },
                    }
                };
                await UploadAsync(file, download.Bytes);
                Context.GetLogger().Info($"Persisted {download.DocumentId} {file.Name}");
            });
        }

        private async Task<string> CreateFolder(string parentId, string name)
        {
            var list = _service.Files.List();
            list.Q = $"'{parentId}' in parents and name = '{name}' and mimeType = 'application/vnd.google-apps.folder'";
            list.Fields = "files(id)";
            var response = await list.ExecuteAsync();
            var id = response.Files
                .Select(x => x.Id)
                .FirstOrDefault();

            if (id == null)
            {
                var file = new Google.Apis.Drive.v3.Data.File
                {
                    Name = name,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new[] { parentId },
                };
                var request = _service.Files.Create(file);
                request.Fields = "id";
                var result = request.Execute();
                id = result.Id;
            }

            return _folderIdsByName[name] = id;
        }

        private async Task UploadAsync(Google.Apis.Drive.v3.Data.File file, byte[] bytes)
        {
            var list = _service.Files.List();
            list.Q = $"'{file.Parents[0]}' in parents and name = '{file.Name}'";
            list.Fields = "files(id)";
            var response = await list.ExecuteAsync();
            var id = response.Files
                .Select(x => x.Id)
                .FirstOrDefault();

            using (var stream = new MemoryStream(bytes))
            {
                if (id == null)
                {
                    var create = _service.Files.Create(file, stream, file.MimeType);
                    create.KeepRevisionForever = true;
                    await create.UploadAsync();
                }
                else
                {
                    var update = _service.Files.Update(file, id, stream, file.MimeType);
                    update.KeepRevisionForever = true;
                    await update.UploadAsync();
                }
            }
        }
    }
}
