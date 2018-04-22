using System;
using Akka.Actor;
using Akka.Event;
using Regulations.Gov.Client;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using RestEase;
using System.Threading.Tasks;

namespace Regulations.Gov.Archiver
{
    public class Requester : ReceiveActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }
        private readonly string _key;
        private const int ResultsPerPage = 1000;
        public static TimeSpan GetRecentTimeout = TimeSpan.FromMinutes(5);
        public static TimeSpan RestPeriod = TimeSpan.FromMinutes(1);

        public Requester(RegulationsGovClient client, IStorageService storageService)
        {
            _key = $"{nameof(Requester)}.{nameof(Status)}.json";
            Become(() => Initializing(client, storageService));
        }

        private void Initializing(RegulationsGovClient client, IStorageService storageService)
        {
            ReceiveAsync<string>(async _ =>
            {
                await storageService.TouchAsync(_key);
                var bytes = await storageService.ReadAsync(_key);
                var json = Encoding.UTF8.GetString(bytes);
                var status = JsonConvert.DeserializeObject<Status>(json);

                Become(() => Requesting(client, storageService, Sender));

                if (status?.TotalNumRecords == null)
                {
                    var result = await client.GetDocuments(new DocumentsQuery
                    {
                        ResultsPerPage = 0,
                        PageOffset = 0,
                    });

                    status = new Status
                    {
                        TotalNumRecords = result.TotalNumRecords,
                        HistoricalPageOffsets = Enumerable.Range(0, (int)Math.Ceiling((double)result.TotalNumRecords / ResultsPerPage))
                            .Select(i => i * ResultsPerPage)
                            .ToHashSet(),
                    };
                }

                Self.Tell(status);
            });
        }

        private void Requesting(RegulationsGovClient client, IStorageService storageService, IActorRef coordinator)
        {
            Status status = null;
            async Task WriteStatusAsync(Status message)
            {
                var json = JsonConvert.SerializeObject(message);
                var bytes = Encoding.UTF8.GetBytes(json);
                await storageService.WriteAsync(_key, bytes);
                status = message;
            }

            async Task RequestWithBackoffAsync(BaseGet message, Action<DocumentsResult> action)
            {
                try
                {
                    var result = await client.GetDocuments(new DocumentsQuery
                    {
                        SortBy = SortFields.PostedDate,
                        SortOrder = SortOrderType.Ascending,
                        ResultsPerPage = ResultsPerPage,
                        PageOffset = message.PageOffset,
                    });

                    if (result.Documents != null)
                    {
                        coordinator.Tell(result.Documents
                            .Select(document => new Persistable<DocumentStub>
                            {
                                Id = document.DocumentId,
                                RetrievedAt = DateTimeOffset.Now,
                                Value = document,
                            })
                            .ToList());
                    }
                    action(result);
                }
                catch (ApiException ae) when ((int)ae.StatusCode == 429)
                {
                    Context.GetLogger().Info($"Too many requests; backing off");

                    Stash.Prepend(new[] { new Envelope(message, Sender) });
                    Become(() => Resting(client, storageService, coordinator));
                }
            }

            ReceiveAsync<Status>(async message =>
            {
                await WriteStatusAsync(message);

                Self.Tell(new GetRecent { PageOffset = status.TotalNumRecords.Value });
                status.HistoricalPageOffsets
                    .OrderByDescending(i => i)
                    .Select(pageOffset => new GetHistorical { PageOffset = pageOffset })
                    .ToList()
                    .ForEach(Self.Tell);
            });

            Receive<CheckForNewRecent>(_ =>
            {
                Self.Tell(new GetRecent { PageOffset = status.TotalNumRecords.Value });
            });

            ReceiveAsync<GetRecent>(async message =>
            {
                await RequestWithBackoffAsync(message, async result =>
                {
                    if (result.TotalNumRecords > status.TotalNumRecords)
                    {
                        var diff = result.TotalNumRecords - status.TotalNumRecords;
                        var newStatus = new Status
                        {
                            TotalNumRecords = result.TotalNumRecords,
                            HistoricalPageOffsets = Enumerable.Range(status.TotalNumRecords.Value, (int)Math.Ceiling((double)diff / ResultsPerPage))
                                .Select(i => i * ResultsPerPage)
                                .ToHashSet(),
                        };
                        await WriteStatusAsync(newStatus);
                    }
                });

            });

            ReceiveAsync<GetHistorical>(async message =>
            {
                await RequestWithBackoffAsync(message, async result =>
                {
                    var newStatus = new Status
                    {
                        TotalNumRecords = status.TotalNumRecords,
                        HistoricalPageOffsets = status.HistoricalPageOffsets
                            .Except(new[] { message.PageOffset })
                            .ToHashSet(),
                    };
                    await WriteStatusAsync(newStatus);
                });
            });

            Context.System.Scheduler.ScheduleTellRepeatedly(GetRecentTimeout, GetRecentTimeout, Self, new CheckForNewRecent(), Self);
        }

        private void Resting(RegulationsGovClient client, IStorageService storageService, IActorRef coordinator)
        {
            Receive<string>(_ =>
            {
                Context.GetLogger().Info("Resuming requests");
                Stash.UnstashAll();
                Become(() => Requesting(client, storageService, coordinator));
            });

            ReceiveAny(_ => Stash.Stash());

            Context.System.Scheduler.ScheduleTellOnce(RestPeriod, Self, "resume", Self);
        }

        private abstract class BaseGet
        {
            public int PageOffset { get; set; }

        }
        private class GetRecent : BaseGet { }
        private class GetHistorical : BaseGet { }
        private class CheckForNewRecent { }

        private class Status
        {
            public ISet<int> HistoricalPageOffsets { get; set; } = new HashSet<int>();
            public int? TotalNumRecords { get; set; }
        }
    }
}
