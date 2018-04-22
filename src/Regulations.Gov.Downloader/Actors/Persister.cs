using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Event;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Newtonsoft.Json;

namespace Regulations.Gov.Downloader.Actors
{
    public class Persister : ReceiveActor
    {
        public Persister(GoogleSettings settings)
        {
            var credential = GoogleCredential.FromFile(settings.KeyJsonPath)
                .CreateScoped(DriveService.Scope.Drive)
                .CreateWithUser(settings.User);

            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = GetType().FullName,
            });
            var folderIdsByName = new Dictionary<string, string>();
            string GetOrAddFolder(string name, string parentId = null)
            {
                const string folderMimeType = "application/vnd.google-apps.folder";

                if (folderIdsByName.TryGetValue(name, out var folderId)) return folderId;

                var request = service.Files.List();
                request.Q = $"mimeType = '{folderMimeType}'";
                if (parentId != null)
                {
                    request.Q += $" and '{parentId}' in parents";
                }
                request.Spaces = "drive";
                request.Fields = "files(name,id)";
                var result = request.Execute();

                foreach (var file in result.Files)
                {
                    folderIdsByName[file.Name] = file.Id;
                }

                if (folderIdsByName.TryGetValue(name, out folderId)) return folderId;

                var newFolder = new Google.Apis.Drive.v3.Data.File
                {
                    Name = name,
                    MimeType = folderMimeType,
                };
                if (parentId != null)
                {
                    newFolder.Parents = new[] { parentId };
                }
                var newFolderRequest = service.Files.Create(newFolder);
                newFolderRequest.Fields = "id";
                var newFolderResult = newFolderRequest.Execute();
                return folderIdsByName[name] = newFolderResult.Id;
            }
            var rootFolder = GetOrAddFolder(settings.Path);

            ReceiveAsync<DocumentManifest>(async documentManifest =>
            {
                var documentId = documentManifest.DocumentReference["documentId"] as string;
                var documentFolderId = GetOrAddFolder(documentId, rootFolder);
                var referenceJson = JsonConvert.SerializeObject(documentManifest.DocumentReference);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(referenceJson)))
                {
                    var file = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = $"reference.json",
                        Parents = new[] { documentFolderId },
                        MimeType = "application/json",
                    };
                    var request = service.Files.Create(file, stream, file.MimeType);
                    await request.UploadAsync();
                    Context.GetLogger().Info($"Persisted {documentId} {file.Name}");
                }
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(documentManifest.DocumentJson)))
                {
                    var file = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = $"document.json",
                        Parents = new[] { documentFolderId },
                        MimeType = "application/json",
                    };
                    var request = service.Files.Create(file, stream, file.MimeType);
                    await request.UploadAsync();
                    Context.GetLogger().Info($"Persisted {documentId} {file.Name}");
                }
            });

            ReceiveAsync<Download>(async download =>
            {
                var documentFolderId = GetOrAddFolder(download.DocumentId, rootFolder);
                using (var stream = new MemoryStream(download.Bytes))
                {
                    var sanitizedOriginalFileName = Regex.Replace(download.OriginalFileName, @"\W", "-");
                    var file = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = $"{download.DocumentId}-{download.Tag}-{sanitizedOriginalFileName}",
                        Parents = new[] { documentFolderId },
                        MimeType = download.ContentType,
                    };
                    var request = service.Files.Create(file, stream, file.MimeType);
                    await request.UploadAsync();
                    Context.GetLogger().Info($"Persisted {download.DocumentId} {file.Name}");
                }
            });
        }

        #region Messages
        public class DocumentManifest
        {
            public IDictionary<string, object> DocumentReference { get; set; }
            public string DocumentJson { get; set; }
        }
        public class Download
        {
            public string DocumentId { get; set; }
            public byte[] Bytes { get; set; }
            public string Tag { get; set; }
            public string ContentType { get; set; }
            public string OriginalFileName { get; set; }
        }
        #endregion
    }
}
