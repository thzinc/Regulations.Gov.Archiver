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
        public Coordinator()
        {
            Context.GetLogger().Info("Running coordinator");
            var requester = Context.ActorOf(Context.DI().Props<Requester>());
            var archiveRequester = Context.ActorOf(Context.DI().Props<Requester>());
            var persister = Context.ActorOf(Context.DI().Props<Persister>());

            Receive<PersistFile>(download => persister.Tell(download));

            ReceiveAny(_ => Context.GetLogger().Info("Received unknown message"));

            // TODO: Make this configurable
            archiveRequester.Tell(new GetDocuments());

            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromDays(1), requester, new GetRecentDocuments(), Self);
        }

        #region Messages

        #endregion
    }
}
