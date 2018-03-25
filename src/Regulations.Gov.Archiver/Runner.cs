using System;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Regulations.Gov.Archiver
{
    public class Runner : IDisposable
    {
        private ActorSystem _actorSystem;
        private ManualResetEventSlim _mre;
        private readonly Uri elasticSearchUrl;
        private readonly string apiKey;

        public Runner(Uri elasticSearchUrl, string apiKey)
        {
            this.elasticSearchUrl = elasticSearchUrl;
            this.apiKey = apiKey;
        }

        public void Start()
        {
            _actorSystem = ActorSystem.Create("Regulations-Gov-Archiver");
            _actorSystem.ActorOf(Props.Create(() => new Archiver(elasticSearchUrl, apiKey)));
            _mre = new ManualResetEventSlim();
        }

        public void Wait()
        {
            _mre.Wait();
        }

        public void Stop()
        {
            _mre.Set();
        }

        public void Dispose()
        {
            if (_actorSystem != null) _actorSystem.Dispose();
        }
    }
}
