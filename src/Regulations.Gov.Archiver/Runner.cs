using System;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Logger.Serilog;
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
        private readonly string downloadPath;

        public Runner(Uri elasticSearchUrl, string apiKey, string downloadPath)
        {
            this.elasticSearchUrl = elasticSearchUrl;
            this.apiKey = apiKey;
            this.downloadPath = downloadPath;
        }

        public void Start()
        {
            var config = ConfigurationFactory.FromObject(new
            {
                akka = new
                {
                    loggers = new[]
                    {
                        typeof(SerilogLogger).AssemblyQualifiedName,
                    },
                },
            });
            _actorSystem = ActorSystem.Create("Regulations-Gov-Archiver");
            _actorSystem.ActorOf(Props.Create(() => new Archiver(elasticSearchUrl, apiKey, downloadPath)));
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
