using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Nest;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Logging;
using Autofac;

namespace Regulations.Gov.Archiver
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

            var apiKey = Configuration["DataGovApiKey"];
            var elasticSearchUrl = new Uri(Configuration["ElasticsearchUrl"]);
            var downloadPath = Configuration["DownloadPath"];

            // Setup Autofac
            ContainerBuilder builder = new ContainerBuilder();
            builder.RegisterInstance<ElasticClient>(GetElasticClient(elasticSearchUrl));
            var container = builder.Build();

            using (var runner = new Runner(container))
            {
                logger.LogInformation("Starting Regulations.Gov.Archiver...");
                runner.Start();
                AssemblyLoadContext.Default.Unloading += _ => runner.Stop();
                Console.CancelKeyPress += (_, ea) =>
                {
                    runner.Stop();
                    ea.Cancel = true;
                };
                logger.LogInformation("Regulations.Gov.Archiver started!");

                runner.Wait();

                logger.LogInformation("Stopping Regulations.Gov.Archiver...");
            }

            logger.LogInformation("Regulations.Gov.Archiver stopped!");
        }

        private static ElasticClient GetElasticClient(Uri elasticSearchUrl)
        {
            var config = new ConnectionSettings(elasticSearchUrl);
            var elasticClient = new ElasticClient(config);
            return elasticClient;
        }
    }
}
