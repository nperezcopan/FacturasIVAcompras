using FacturasIvaCompra.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FacturasIvaCompra.Infrastructure.Services
{
    public class FileMoverService : IFileMover
    {
        private readonly IConfiguration _configuration;

        public FileMoverService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string MoveToProcessed(string sourceFilePath)
        {
            var processedFolder = _configuration["FolderSettings:ProcessedFolder"]
                ?? throw new InvalidOperationException("Falta configurar FolderSettings:ProcessedFolder en appsettings.json.");

            Directory.CreateDirectory(processedFolder);

            var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            var extension = Path.GetExtension(sourceFilePath);
            var targetPath = Path.Combine(processedFolder, Path.GetFileName(sourceFilePath));

            var suffix = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(processedFolder, $"{fileName}_{suffix}{extension}");
                suffix++;
            }

            File.Move(sourceFilePath, targetPath);
            return targetPath;
        }
    }
}
