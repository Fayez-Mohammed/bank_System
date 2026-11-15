using Castle.Core.Logging;
using DocumentFormat.OpenXml.Wordprocessing;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Implementations
{
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly string _rootPath;
        private readonly ILogger<LocalFileStorageService> _logger;
       

        public LocalFileStorageService(IConfiguration configuration, ILogger<LocalFileStorageService> logger)
        {
            _rootPath = configuration["FileStorage:RootPath"]
                        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "data-files");
            _logger = logger;
            Directory.CreateDirectory(_rootPath);
        }

        public async Task<string> SaveAsync(Stream content, string suggestedFileName, string subFolder, CancellationToken cancellationToken = default)
        {
            var targetFolder = Path.Combine(_rootPath, subFolder);
            Directory.CreateDirectory(targetFolder);

            var extension = Path.GetExtension(suggestedFileName);
            var storedName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(targetFolder, storedName);

            await using var fileStream = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await content.CopyToAsync(fileStream, cancellationToken);

            _logger.LogInformation("Stored file {OriginalName} at {Path}", suggestedFileName, fullPath);

            return Path.Combine(subFolder, storedName).Replace('\\', '/');
        }

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(_rootPath, storagePath);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Requested stored file not found: {Path}", fullPath);
                throw new FileNotFoundException("الملف المطلوب مش موجود في التخزين.", fullPath);
            }

            Stream stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(_rootPath, storagePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted stored file {Path}", fullPath);
            }
            return Task.CompletedTask;
        }

        public Task DeleteFolderAsync(string subFolder, CancellationToken cancellationToken = default)
        {
            var root = Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, subFolder)).TrimEnd(Path.DirectorySeparatorChar);

            // حماية: ما نمسحش الـ root نفسه ولا أي حاجة برّاه
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid storage folder.");

            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
                _logger.LogInformation("Deleted stored folder {Path}", full);
            }
            return Task.CompletedTask;
        }
    }
}
