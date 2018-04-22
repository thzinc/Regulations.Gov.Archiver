using Akka.Actor;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestEase;
using System;
using System.Net;
using Akka.Event;
using System.Net.Http;
using static Regulations.Gov.Downloader.Actors.Persister;
using System.Linq;
using System.IO;

namespace Regulations.Gov.Downloader.Actors
{
    public class Requester : ReceiveActor, IWithUnboundedStash
    {
        private readonly TimeSpan HardWaitPeriod = TimeSpan.FromMinutes(5);
        private readonly int SoftWaitRequestThreshold = 5;
        private readonly TimeSpan SoftWaitPeriod = TimeSpan.FromMinutes(1);
        public IStash Stash { get; set; }
        private readonly IRegulationsGovApi _client;
        private readonly DataGovCredentials _credentials;

        public Requester(DataGovCredentials credentials)
        {
            _client = RestClient.For<IRegulationsGovApi>("https://api.data.gov");
            this._credentials = credentials;

            Become(() => Requesting());
        }

        private void Requesting()
        {
            int? lastRateLimitRemaining = null;
            async Task<(bool Success, Response<T>)> GetResponse<TRequest, T>(TRequest message, Func<TRequest, Task<Response<T>>> request)
            {
                Context.GetLogger().Debug($"Expected rate limited requests remaining: {lastRateLimitRemaining}");

                var response = await request(message);
                response.ResponseMessage.Headers.TryGetValues("X-RateLimit-Remaining", out var rateLimitRemainings);
                if (int.TryParse(rateLimitRemainings.FirstOrDefault(), out var rateLimitRemaining))
                {
                    lastRateLimitRemaining = rateLimitRemaining;
                    Context.GetLogger().Debug($"Rate limited requests remaining: {lastRateLimitRemaining}");
                }

                if ((int)response.ResponseMessage.StatusCode == 429)
                {
                    Stash.Stash();
                    Become(() => Waiting(HardWaitPeriod));
                    return (false, null);
                }
                else if (!response.ResponseMessage.IsSuccessStatusCode)
                {
                    Context.GetLogger().Error(response.StringContent);
                    Context.GetLogger().Info("Going to attempt this message again");
                    Self.Tell(message);
                    return (false, null);
                }

                if (lastRateLimitRemaining.HasValue && lastRateLimitRemaining.Value < SoftWaitRequestThreshold)
                {
                    Become(() => Waiting(SoftWaitPeriod));
                }

                return (true, response);
            }

            ReceiveAsync<GetRecentDocuments>(async request =>
            {
                var (success, response) = await GetResponse(request, r => _client.GetRecentDocumentsAsync(_credentials.ApiKey, 0, r.PageOffset));
                if (success)
                {
                    var documentsResponse = response.GetContent();
                    documentsResponse.Documents.ForEach(Self.Tell);

                    var pageOffset = request.PageOffset + documentsResponse.Documents.Count;
                    if (pageOffset < documentsResponse.TotalNumRecords)
                    {
                        Self.Tell(new GetRecentDocuments
                        {
                            PageOffset = pageOffset,
                        });
                    }
                }
            });

            ReceiveAsync<DocumentReference>(async documentReference =>
            {
                var (success, response) = await GetResponse(documentReference, dr => _client.GetDocumentAsync(_credentials.ApiKey, dr.DocumentId));
                if (success)
                {
                    var document = response.GetContent();
                    var json = await response.ResponseMessage.Content.ReadAsStringAsync();
                    Context.Parent.Tell(new DocumentManifest
                    {
                        DocumentReference = documentReference,
                        DocumentJson = json,
                    });

                    document.Attachments
                        .SelectMany(a => a.FileFormats
                            .Select((ff, i) => new GetDownload
                            {
                                DocumentId = documentReference.DocumentId,
                                Tag = $"attachment.{a.AttachmentOrderNumber}-{i}",
                                Url = ff,
                            }))
                        .ToList()
                        .ForEach(Self.Tell);
                    document.FileFormats
                        .Select((ff, i) => new GetDownload
                        {
                            DocumentId = documentReference.DocumentId,
                            Tag = $"download.{i}",
                            Url = ff,
                        })
                        .ToList()
                        .ForEach(Self.Tell);
                }
            });

            ReceiveAsync<GetDownload>(async request =>
            {
                var (success, response) = await GetResponse(request, r => _client.GetDownloadAsync(_credentials.ApiKey, r.Url));
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
                    Context.Parent.Tell(new Download
                    {
                        DocumentId = request.DocumentId,
                        Tag = request.Tag,
                        Bytes = bytes,
                        OriginalFileName = originalFileName,
                        ContentType = contentType,
                    });
                }
            });

            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromDays(1), Self, new GetRecentDocuments(), Self);
        }

        private void Waiting(TimeSpan waitPeriod)
        {
            Receive<Resume>(_ =>
            {
                Stash.UnstashAll();
                Become(() => Requesting());
            });

            ReceiveAny(_ => Stash.Stash());

            Context.GetLogger().Warning($"Waiting for {waitPeriod} before continuing requests");
            Context.System.Scheduler.ScheduleTellOnce(waitPeriod, Self, new Resume(), Self);
        }

        #region Messages
        public class Resume { }
        public class GetRecentDocuments
        {
            public int PageOffset { get; set; }
        }
        public class GetDownload
        {
            public string DocumentId { get; set; }
            public string Tag { get; set; }
            public string Url { get; set; }
        }
        #endregion

        [AllowAnyStatusCode]
        public interface IRegulationsGovApi
        {
            [Get("/regulations/v3/documents.json?rpp=1000")]
            Task<Response<DocumentsResponse>> GetRecentDocumentsAsync(string api_key, int daysSinceModified, int po);

            [Get("/regulations/v3/documents.json?rpp=1000&sb=postedDate&so=ASC")]
            Task<Response<DocumentsResponse>> GetDocumentsAsync(string api_key, int po);

            [Get("/regulations/v3/document.json")]
            Task<Response<Document>> GetDocumentAsync(string api_key, string documentId);

            [Get("{url}")]
            Task<Response<Stream>> GetDownloadAsync(string api_key, [Path(UrlEncode = false)]string url);
        }

        public class DocumentsResponse
        {
            public List<DocumentReference> Documents { get; set; } = new List<DocumentReference>();
            public long TotalNumRecords { get; set; }
        }

        public class DocumentReference : Dictionary<string, object>
        {
            public string DocumentId => (string)this["documentId"];
            public string DocketId => (string)this["docketId"];
        }

        public class Document
        {
            public List<Attachment> Attachments { get; set; } = new List<Attachment>();
            public List<string> FileFormats { get; set; } = new List<string>();
        }

        public class Attachment
        {
            public int AttachmentOrderNumber { get; set; }
            public List<string> FileFormats { get; set; } = new List<string>();
        }
    }
}
