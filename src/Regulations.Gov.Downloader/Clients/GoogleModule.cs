using Autofac;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace Regulations.Gov.Downloader.Clients
{
    public class GoogleModule : Module
    {
        private readonly string _keyPath;
        private readonly string _driveUser;
        private readonly string _drivePath;
        public GoogleModule(string keyPath, string driveUser, string drivePath)
        {
            _keyPath = keyPath;
            _driveUser = driveUser;
            _drivePath = drivePath;
        }

        protected override void Load(ContainerBuilder builder)
        {
            var credential = GoogleCredential.FromFile(_keyPath)
                .CreateScoped(DriveService.Scope.Drive)
                .CreateWithUser(_driveUser);

            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = GetType().FullName,
            });

            builder.RegisterInstance(service);
            builder.RegisterInstance(new GoogleSettings
            {
                Path = _drivePath,
            });
        }
    }
}
