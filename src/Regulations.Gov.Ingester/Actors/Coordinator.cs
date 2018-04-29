using Akka.Actor;
using Akka.Event;
using Akka.DI.Core;
using System.Linq;
using System;

namespace Regulations.Gov.Ingester.Actors
{

    public class Coordinator : ReceiveActor
    {
        public Coordinator()
        {
            Context.GetLogger().Info("Running coordinator");
            var downloader = Context.ActorOf(Context.DI().Props<Discoverer>(), "Downloader");
            var ingester = Context.ActorOf(Context.DI().Props<Ingester>(), "Ingester");
            Receive<FileContent>(fileContent => ingester.Tell(fileContent));
            ReceiveAny(_ => Context.GetLogger().Info("Received unknown message"));
        }
    }
}
