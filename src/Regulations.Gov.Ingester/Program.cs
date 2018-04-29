using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Nest;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Logging;
using Autofac;
using GoogleClient;

namespace Regulations.Gov.Ingester
{
    public class Program
    {
        public static ILoggerFactory LoggerFactory { get; }
        public static IConfigurationRoot Configuration { get; }
        static Program()
        {
            Configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables()
                .Build();
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .ReadFrom.Configuration(Configuration)
                .WriteTo.Console()
                .CreateLogger();
            LoggerFactory = new LoggerFactory()
                .AddSerilog();
        }

        public static void Main(string[] args)
        {
            var logger = LoggerFactory.CreateLogger<Program>();

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                logger.LogError(e.Exception, "Unhandled exception");
                e.SetObserved();
            };

            var elasticSearchUrl = new Uri(Configuration["ElasticsearchUrl"]);
            logger.LogInformation($"Using {elasticSearchUrl} for ES");
            
            // Setup Autofac
            ContainerBuilder builder = new ContainerBuilder();
            builder.RegisterModule(new GoogleModule(Configuration["GoogleKeyJsonPath"], Configuration["GoogleDriveUser"], Configuration["GoogleDrivePath"]));
            builder.RegisterInstance<ElasticClient>(GetElasticClient(elasticSearchUrl));
            builder.RegisterType<Actors.Coordinator>();
            builder.RegisterType<Actors.Discoverer>();
            builder.RegisterType<Actors.Downloader>();
            builder.RegisterType<Actors.Ingester>();
            var container = builder.Build();

            using (var runner = new Runner(container))
            {
                logger.LogInformation("Starting Regulations.Gov.Ingester...");
                runner.Start();
                AssemblyLoadContext.Default.Unloading += _ => runner.Stop();
                Console.CancelKeyPress += (_, ea) =>
                {
                    runner.Stop();
                    ea.Cancel = true;
                };
                logger.LogInformation("Regulations.Gov.Ingester started!");

                runner.Wait();

                logger.LogInformation("Stopping Regulations.Gov.Ingester...");
            }

            logger.LogInformation("Regulations.Gov.Ingester stopped!");
        }

        private static ElasticClient GetElasticClient(Uri elasticSearchUrl)
        {
            var config = new ConnectionSettings(elasticSearchUrl);
            var elasticClient = new ElasticClient(config);
            if (!elasticClient.CatHealth().IsValid) throw new InvalidOperationException($"Elasticsearch is not healthy! {elasticSearchUrl}");
            return elasticClient;
        }
    }
}
