using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Logging;
using Autofac;
using Regulations.Gov.Downloader.Clients;
using GoogleClient;

namespace Regulations.Gov.Downloader
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

            ContainerBuilder builder = new ContainerBuilder();
            builder.RegisterModule(new RegulationsGovClientModule(Configuration["DataGovApiKey"]));
            builder.RegisterModule(new GoogleModule(Configuration["GoogleKeyJsonPath"], Configuration["GoogleDriveUser"], Configuration["GoogleDrivePath"]));
            builder.RegisterType<Actors.Coordinator>();
            builder.RegisterType<Actors.Requester>();
            builder.RegisterType<Actors.Persister>();
            var container = builder.Build();

            using (var runner = new Runner(container))
            {
                logger.LogInformation("Starting Regulations.Gov.Downloader...");
                runner.Start();
                AssemblyLoadContext.Default.Unloading += _ => runner.Stop();
                Console.CancelKeyPress += (_, ea) =>
                {
                    runner.Stop();
                    ea.Cancel = true;
                };
                logger.LogInformation("Regulations.Gov.Downloader started!");

                runner.Wait();

                logger.LogInformation("Stopping Regulations.Gov.Downloader...");
            }

            logger.LogInformation("Regulations.Gov.Downloader stopped!");
        }
    }
}
