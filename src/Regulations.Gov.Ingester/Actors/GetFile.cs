using Akka.Actor;
using Google.Apis.Drive.v3.Data;

namespace Regulations.Gov.Ingester.Actors
{
    public class GetFile
    {
        public File File { get; set; }
        public string DocumentId { get; set; }
        public IActorRef Target { get; set; }
    }
}
