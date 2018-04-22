using Autofac;
using RestEase;

namespace Regulations.Gov.Downloader.Clients
{
    public class RegulationsGovClientModule : Module
    {
        private readonly string _apiKey;

        public RegulationsGovClientModule(string apiKey)
        {
            this._apiKey = apiKey;
        }

        protected override void Load(ContainerBuilder builder)
        {
            var client = RestClient.For<IRegulationsGovApi>("https://api.data.gov");
            client.ApiKey = _apiKey;
            builder.RegisterInstance(client);
        }
    }
}
