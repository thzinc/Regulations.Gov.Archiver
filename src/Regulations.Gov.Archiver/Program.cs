using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Nest;
using Regulations.Gov.Client;

namespace Regulations.Gov.Archiver
{
    public class Program
    {
        public static void Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.Error.WriteLine($"Unhandled exception: {e.Exception.Message}");
                Console.Error.WriteLine(e.Exception.StackTrace);
                e.SetObserved();
            };

            var apiKey = Environment.GetEnvironmentVariable("DataGovApiKey");
            var elasticSearchUrl = new Uri(Environment.GetEnvironmentVariable("ElasticsearchUrl"));
            
            using (var runner = new Runner(elasticSearchUrl, apiKey))
            {
                Console.Write("Starting Regulations.Gov.Archiver...");
                runner.Start();
                AssemblyLoadContext.Default.Unloading += _ => runner.Stop();
                Console.CancelKeyPress += (_, ea) => {
                    runner.Stop();
                    ea.Cancel = true;
                };
                Console.WriteLine(" started!");

                runner.Wait();

                Console.Write("Stopping Regulations.Gov.Archiver...");
            }

            Console.WriteLine(" stopped!");
        }
    }
}
