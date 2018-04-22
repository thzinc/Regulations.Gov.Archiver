using System;
using System.IO;
using System.Threading.Tasks;

namespace Regulations.Gov.Archiver
{
    public interface IStorageService
    {
        Task<byte[]> ReadAsync(string key);
        Task TouchAsync(string key);
        Task WriteAsync(string key, byte[] bytes);
    }

    public class StorageServiceSettings
    {
        public string Path { get; set; }
    }

    public class StorageService : IStorageService
    {
        private readonly StorageServiceSettings settings;

        public StorageService(StorageServiceSettings settings)
        {
            this.settings = settings;
        }

        public Task TouchAsync(string key)
        {
            using (File.Create(Path.Combine(settings.Path, key)))
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReadAsync(string key)
        {
            return await File.ReadAllBytesAsync(Path.Combine(settings.Path, key));
        }

        public async Task WriteAsync(string key, byte[] bytes)
        {
            await File.WriteAllBytesAsync(Path.Combine(settings.Path, key), bytes);
        }
    }
}
