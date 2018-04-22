using Akka.Actor;
using System.Threading.Tasks;
using RestEase;
using System;
using System.Net;
using Akka.Event;
using System.Net.Http;
using static Regulations.Gov.Downloader.Actors.Persister;
using System.Linq;
using Newtonsoft.Json;
using System.Text;
using Regulations.Gov.Downloader.Clients;
using System.Collections.Generic;

namespace Regulations.Gov.Downloader.Actors
{
    public class Requester : ReceiveActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }
        private readonly TimeSpan HardWaitPeriod = TimeSpan.FromMinutes(5);
        private readonly int SoftWaitRequestThreshold = 5;
        private readonly TimeSpan SoftWaitPeriod = TimeSpan.FromMinutes(1);
        private readonly int DaysSinceModified = 7;
        private readonly IRegulationsGovApi _client;
        public Requester(IRegulationsGovApi client)
        {
            _client = client;
            Become(() => Discovering());
        }

        private void Discovering()
        {
            var documentIds = new HashSet<string>();
            ReceiveAsync<GetRecentDocuments>(async request =>
            {
                var (success, response) = await GetResponse(request, r => _client.GetRecentDocumentsAsync(DaysSinceModified, r.PageOffset));
                if (success)
                {
                    var documentsResponse = response.GetContent();
                    var pageOffset = request.PageOffset + documentsResponse.Documents.Count;
                    if (pageOffset < documentsResponse.TotalNumRecords)
                    {
                        Self.Tell(new GetRecentDocuments
                        {
                            PageOffset = pageOffset,
                        });
                    }
                    else
                    {
                        Become(() => Downloading(documentIds));
                    }

                    foreach (var documentReference in documentsResponse.Documents)
                    {
                        documentIds.Add(documentReference.DocumentId);
                        var json = JsonConvert.SerializeObject(documentReference as IDictionary<string, object>);
                        Context.Parent.Tell(new PersistFile
                        {
                            Bytes = Encoding.UTF8.GetBytes(json),
                            ContentType = "application/json",
                            DocumentId = documentReference.DocumentId,
                            Tag = "reference",
                            OriginalFileName = "reference.json",
                        });
                    }
                }
            });

            Context.GetLogger().Info("Waiting to discover documents...");
        }

        private void Downloading(HashSet<string> documentIds)
        {
            ReceiveAsync<string>(async documentId =>
            {
                var (success, response) = await GetResponse(documentId, dr => _client.GetDocumentAsync(documentId));
                if (success)
                {
                    var document = response.GetContent();
                    var bytes = await response.ResponseMessage.Content.ReadAsByteArrayAsync();
                    Context.Parent.Tell(new PersistFile
                    {
                        Bytes = bytes,
                        ContentType = "application/json",
                        DocumentId = documentId,
                        Tag = "document",
                        OriginalFileName = "document.json",
                    });
                    documentIds.Remove(documentId);

                    var attachments = document.Attachments
                        .SelectMany(a => a.FileFormats
                            .Select((ff, i) => new GetDownload
                            {
                                DocumentId = documentId,
                                Tag = $"attachment.{a.AttachmentOrderNumber}-{i}",
                                Url = ff,
                            }));
                    var downloads = document.FileFormats
                        .Select((ff, i) => new GetDownload
                        {
                            DocumentId = documentId,
                            Tag = $"download.{i}",
                            Url = ff,
                        });

                    foreach (var getDownloadRequest in attachments.Concat(downloads))
                    {
                        documentIds.Add(getDownloadRequest.Key);
                    }
                }

                if (!documentIds.Any())
                {
                    Stash.UnstashAll();
                    Become(() => Discovering());
                }
            });

            ReceiveAsync<GetDownload>(async request =>
            {
                var (success, response) = await GetResponse(request, r => _client.GetDownloadAsync(r.Url));
                if (success)
                {
                    var bytes = await response.ResponseMessage.Content.ReadAsByteArrayAsync();
                    response.ResponseMessage.Headers.TryGetValues("Content-Disposition", out var contentDispositions);
                    var originalFileName = contentDispositions.FirstOrDefault()?
                        .Split(";", 2).Skip(1)
                        .SelectMany(s => s.Split("=", 2).Skip(1))
                        .Select(s => s.Trim('"'))
                        .FirstOrDefault();
                    response.ResponseMessage.Headers.TryGetValues("Content-Type", out var contentTypes);
                    var contentType = contentTypes.FirstOrDefault();
                    Context.Parent.Tell(new PersistFile
                    {
                        DocumentId = request.DocumentId,
                        Tag = request.Tag,
                        Bytes = bytes,
                        OriginalFileName = originalFileName,
                        ContentType = contentType,
                    });
                    documentIds.Remove(request.Key);
                }

                if (!documentIds.Any())
                {
                    Stash.UnstashAll();
                    Become(() => Discovering());
                }
            });

            ReceiveAny(_ => Stash.Stash());

            Context.GetLogger().Info($"Downloading {documentIds.Count} documents...");
            documentIds
                .ToList()
                .ForEach(Self.Tell);
        }

        private void Waiting(TimeSpan waitPeriod)
        {
            Receive<Resume>(_ =>
            {
                Stash.UnstashAll();
                UnbecomeStacked();
            });

            ReceiveAny(_ => Stash.Stash());

            Context.GetLogger().Warning($"Waiting for {waitPeriod} before continuing requests");
            Context.System.Scheduler.ScheduleTellOnce(waitPeriod, Self, new Resume(), Self);
        }

        private async Task<(bool Success, Response<T>)> GetResponse<TRequest, T>(TRequest message, Func<TRequest, Task<Response<T>>> request)
        {
            var response = await request(message);
            response.ResponseMessage.Headers.TryGetValues("X-RateLimit-Remaining", out var rateLimitRemainings);
            if (int.TryParse(rateLimitRemainings.FirstOrDefault(), out var rateLimitRemaining))
            {
                Context.GetLogger().Debug($"rateLimitRemaining: {rateLimitRemaining}");
            }
            else
            {
                rateLimitRemaining = int.MaxValue;
            }

            if ((int)response.ResponseMessage.StatusCode == 429)
            {
                Context.GetLogger().Warning($"Over rate limit request: {response.StringContent}");
                Stash.Stash();
                BecomeStacked(() => Waiting(HardWaitPeriod));
                return (false, null);
            }
            else if (!response.ResponseMessage.IsSuccessStatusCode)
            {
                Context.GetLogger().Error(response.StringContent);
                Context.GetLogger().Info("Going to attempt this message again");
                Self.Tell(message);
                return (false, null);
            }

            if (rateLimitRemaining < SoftWaitRequestThreshold)
            {
                Become(() => Waiting(SoftWaitPeriod));
            }

            return (true, response);
        }

        #region Messages
        private class Resume { }
        private class GetDownload
        {
            public string DocumentId { get; set; }
            public string Tag { get; set; }
            public string Url { get; set; }
            public string Key => $"{DocumentId}.{Tag}";
        }
        #endregion
    }
}
