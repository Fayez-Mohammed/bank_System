public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string suggestedFileName, string subFolder, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}