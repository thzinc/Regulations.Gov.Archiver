using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using DestructureExtensions;
using ExpressMapper.Extensions;
using Regulations.Gov.Client;
using RestEase;

namespace Regulations.Gov.Archiver
{
    public class RegulationGovClient : ReceiveActor, IWithUnboundedStash
    {
        public static TimeSpan RestPeriod = TimeSpan.FromMinutes(1);
        public IStash Stash { get; set; }
        private Stack<DateTimeOffset> _requests { get; set; } = new Stack<DateTimeOffset>();

        public RegulationGovClient(string apiKey)
        {
            var apiClient = new RegulationsGovClient(apiKey);
            Become(() => Requesting(apiClient));
        }

        private void Requesting(RegulationsGovClient apiClient)
        {
            Func<TMessage, Task> HandleRequestLimitingAsync<TMessage>(Func<TMessage, Task> handle)
            {
                return async message =>
                {
                    var logger = Context.GetLogger();
                    try
                    {
                        await handle(message);
                        _requests.Push(DateTimeOffset.Now);
                    }
                    catch (ApiException ae) when ((int)ae.StatusCode == 429)
                    {
                        var now = DateTimeOffset.Now;
                        var hourAgo = now.AddHours(-1);
                        var minuteAgo = now.AddMinutes(-1);
                        _requests = new Stack<DateTimeOffset>(_requests.Where(r => r >= hourAgo));
                        var requestsPerHour = _requests.Count();
                        var requestsPerMinute = _requests.Count(r => r >= minuteAgo);
                        var last = _requests.Any() ? _requests.Peek() : (DateTimeOffset?)null;
                        var ago = now - last;
                        logger.Info($"Too many requests. ({requestsPerHour}/h, {requestsPerMinute}/m, last request {ago} ago at {last})");

                        Stash.Prepend(new[] { new Envelope(message, Sender) });
                        Become(() => Resting(apiClient));
                    }
                    catch (ApiException ae) when (ae.StatusCode == HttpStatusCode.BadRequest)
                    {
                        logger.Warning($"Bad request for {ae.RequestUri}; skipping");
                    }
                    catch (ApiException ae) when (ae.StatusCode == HttpStatusCode.Forbidden)
                    {
                        logger.Warning($"Forbidden request for {ae.RequestUri}; skipping");
                    }
                    catch (ApiException ae) when (ae.StatusCode == HttpStatusCode.NotFound)
                    {
                        logger.Warning($"Document {ae.RequestUri} not found; skipping");
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Unknown exception; requeuing message");
                        Self.Tell(message);
                    }
                };
            }

            ReceiveAsync(HandleRequestLimitingAsync<DocumentsQuery>(async query =>
            {
                var response = await apiClient.GetDocuments(query);
                Sender.Tell(new DocumentsQueryResult
                {
                    Query = query,
                    Result = response,
                });
            }));

            ReceiveAsync(HandleRequestLimitingAsync<GetDownload>(async request =>
            {
                using (var response = await apiClient.GetDownload(request.Document.Id))
                {
                    if (!response.IsSuccessStatusCode) return;
                    var content = response.Content;
                    var filename = GetContentDispositionFilename(content);
                    var bytes = await content.ReadAsByteArrayAsync();

                    var download = request.Document.Map(new Document());
                    download.DocumentIdType = "DOWNLOAD";
                    download.ReportedContentType = content.Headers.ContentType.ToString();
                    download.ReportedFilename = filename;
                    download.Data = Convert.ToBase64String(bytes);

                    Sender.Tell(download);
                }
            }));

            ReceiveAsync(HandleRequestLimitingAsync<GetAttachment>(async request =>
            {
                using (var response = await apiClient.GetDownload(request.Document.DocumentId, attachmentNumber: request.AttachmentNumber))
                {
                    if (!response.IsSuccessStatusCode) return;
                    var content = response.Content;
                    var filename = GetContentDispositionFilename(content);
                    var bytes = await content.ReadAsByteArrayAsync();


                    var attachment = request.Document.Map(new Document());
                    attachment.DocumentIdType = "ATTACHMENT";
                    attachment.AttachmentNumber = request.AttachmentNumber;
                    attachment.ReportedContentType = content.Headers.ContentType.ToString();
                    attachment.ReportedFilename = filename;
                    attachment.Data = Convert.ToBase64String(bytes);
                    
                    Sender.Tell(attachment);
                }
            }));
        }

        private void Resting(RegulationsGovClient apiClient)
        {
            Receive<string>(_ =>
            {
                Context.GetLogger().Info("Resuming requests");
                Stash.UnstashAll();
                Become(() => Requesting(apiClient));
            });

            ReceiveAny(_ => Stash.Stash());

            Context.System.Scheduler.ScheduleTellOnce(RestPeriod, Self, "resume", Self);
        }

        private static string GetContentDispositionFilename(System.Net.Http.HttpContent content)
        {
            if (content.Headers.TryGetValues("Content-Disposition", out var contentDispositions))
            {
                var (contentDispositionType, properties, _) = contentDispositions.FirstOrDefault()?
                   .Split(';')
                   .Select(s => s.Trim());
                if (contentDispositionType == "attachment")
                {
                    var (propertyType, propertyValue, _) = properties.Split('=');
                    return propertyValue.Trim('"');
                }
            }

            return null;
        }
    }
}
