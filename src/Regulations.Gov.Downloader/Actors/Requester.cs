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
        private readonly TimeSpan HardWaitPeriod = TimeSpan.FromMinutes(1);
        private readonly int SoftWaitRequestThreshold = 5;
        private readonly TimeSpan SoftWaitPeriod = TimeSpan.FromMinutes(0.5);
        private readonly int DaysSinceModified = 7;
        private readonly IRegulationsGovApi _client;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex => Directive.Restart);
        }

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
                var response = await GetResponse(request, r => _client.GetRecentDocumentsAsync(DaysSinceModified, r.PageOffset));
                HandleResponse(documentIds, request, response);
            });

            ReceiveAsync<GetDocuments>(async request =>
            {
                var response = await GetResponse(request, r => _client.GetDocumentsAsync(r.PageOffset));
                HandleResponse(documentIds, request, response);
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
                        Self.Tell(getDownloadRequest);
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
                    var originalFileName = response.ResponseMessage.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                    var contentType = response.ResponseMessage.Content.Headers.ContentType.MediaType;
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
                Context.GetLogger().Error($"Could not download {response.ResponseMessage.RequestMessage.RequestUri}: {response.ResponseMessage.StatusCode} - {response.StringContent}");
                if ((int)response.ResponseMessage.StatusCode / 100 == 5)
                {
                    Context.GetLogger().Info("Requeueing message...");
                    Self.Tell(message);
                }
                return (false, null);
            }

            if (rateLimitRemaining < SoftWaitRequestThreshold)
            {
                Become(() => Waiting(SoftWaitPeriod));
            }

            return (true, response);
        }

        private void HandleResponse<TRequest>(HashSet<string> documentIds, TRequest request, (bool Success, Response<DocumentsResponse>) result)
            where TRequest : GetDocuments, new()
        {
            var (success, response) = result;
            var retry = false;
            if (success)
            {
                try
                {
                    var documentsResponse = response.GetContent();
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

                    var pageOffset = request.PageOffset + documentsResponse.Documents.Count;
                    Context.GetLogger().Info($"Discovered {pageOffset} of {documentsResponse.TotalNumRecords} documents");
                    if (pageOffset < documentsResponse.TotalNumRecords)
                    {
                        Self.Tell(new TRequest
                        {
                            PageOffset = pageOffset,
                        });
                    }
                    else
                    {
                        Become(() => Downloading(documentIds));
                    }
                }
                catch (JsonSerializationException jse)
                {
                    Context.GetLogger().Error(jse, "Caught exception while deserializing; re-requesting...");
                    retry = true;    
                }
                catch (Exception e)
                {
                    Context.GetLogger().Error(e, "Unknown exception; re-requesting...");
                    retry = true;
                }
            }

            if (retry)
            {
                Self.Tell(request);
            }
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
