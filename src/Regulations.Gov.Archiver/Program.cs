using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Nest;
using Regulations.Gov.Client;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Logging;

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

            using (var runner = new Runner(elasticSearchUrl, apiKey, downloadPath))
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
    }
}
