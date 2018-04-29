using Akka.Actor;
using Akka.Event;
using Akka.DI.Core;
using System.Linq;
using System;
using static Regulations.Gov.Downloader.Actors.Persister;

namespace Regulations.Gov.Downloader.Actors
{

    public class Coordinator : ReceiveActor
    {
        public Coordinator(CoordinatorSettings settings)
        {
            Context.GetLogger().Info("Running coordinator");
            var recentDocumentsRequester = Context.ActorOf(Context.DI().Props<Requester>(), "RecentDocumentsRequester");
            var allDocumentsRequester = Context.ActorOf(Context.DI().Props<Requester>(), "AllDocumentsRequester");
            var persister = Context.ActorOf(Context.DI().Props<Persister>());

            Receive<PersistFile>(download => persister.Tell(download));

            ReceiveAny(_ => Context.GetLogger().Info("Received unknown message"));

            if (settings.DownloadAllDocuments)
            {
                allDocumentsRequester.Tell(new GetDocuments());
            }
            else
            {
                Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromDays(1), recentDocumentsRequester, new GetRecentDocuments(), Self);
            }
        }

        #region Messages

        #endregion
    }
}
