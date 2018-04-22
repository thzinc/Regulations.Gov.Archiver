using System;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.DI.AutoFac;
using Akka.Logger.Serilog;
using Autofac;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Regulations.Gov.Archiver
{

    public class Runner : IDisposable
    {
        private ActorSystem _actorSystem;
        private ManualResetEventSlim _mre;
        private IContainer _container;

        public Runner(IContainer container)
        {
            this._container = container;
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
            var resolver = new AutoFacDependencyResolver(_container, _actorSystem);
            // _actorSystem.ActorOf(resolver.Create<Coordinator>(), "Coordinator");
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
