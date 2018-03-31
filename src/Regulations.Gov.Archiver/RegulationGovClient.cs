using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Regulations.Gov.Client;
using RestEase;

namespace Regulations.Gov.Archiver
{
    public class RegulationGovClient : ReceiveActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }
        public static TimeSpan RestPeriod = TimeSpan.FromMinutes(1);

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
                    try
                    {
                        await handle(message);
                    }
                    catch (ApiException ae) when ((int)ae.StatusCode == 429)
                    {
                        Stash.Prepend(new[] { new Envelope(message, Sender) });
                        Become(() => Resting(apiClient));
                    }
                    catch (Exception e)
                    {
                        Context.GetLogger().Error(e, "Caught other exception; retrying");
                        Stash.Prepend(new[] { new Envelope(message, Sender) });
                    }
                };
            }

            ReceiveAsync(HandleRequestLimitingAsync<DocumentsQuery>(async query =>
            {
                var response = await apiClient.GetDocuments(query);
                Sender.Tell(response);
            }));

            ReceiveAsync(HandleRequestLimitingAsync<GetDownload>(async request =>
            {
                var response = await apiClient.GetDownload(request.DocumentId);
                Sender.Tell(response);
            }));
        }

        private void Resting(RegulationsGovClient apiClient)
        {
            Receive<string>(_ =>
            {
                Stash.UnstashAll();
                Become(() => Requesting(apiClient));
            });

            ReceiveAny(_ => Stash.Stash());

            Context.System.Scheduler.ScheduleTellOnce(RestPeriod, Self, "resume", Self);
        }
    }
}
