using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Regulations.Gov.Client;
using RestEase;

namespace Regulations.Gov.Archiver
{
    public class Archiver : ReceiveActor
    {
        public Archiver(System.Uri elasticSearchUrl, string apiKey)
        {
            var apiClient = new RegulationsGovClient(apiKey);
            var indexManager = Context.ActorOf(Props.Create(() => new RegulationIndexManager(elasticSearchUrl)));
            Become(() => Archiving(indexManager, apiClient));
        }

        private void Archiving(IActorRef indexManager, RegulationsGovClient apiClient)
        {
            ReceiveAsync<int>(async pageOffset =>
            {
                try
                {
                    var result = await apiClient.GetDocuments(new DocumentsQuery
                    {
                        SortBy = SortFields.DocId,
                        SortOrder = SortOrderType.Ascending,
                        ResultsPerPage = 1000,
                        PageOffset = pageOffset,
                    });

                    indexManager.Tell(result.Documents);
                    Self.Tell(result.Documents.Count);
                }
                catch (ApiException ae)
                {
                    Context.GetLogger().Error(ae, "Caught API exception");
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMinutes(1), Self, pageOffset, Self);
                }
            });

            Self.Tell(0);
        }
    }
}
