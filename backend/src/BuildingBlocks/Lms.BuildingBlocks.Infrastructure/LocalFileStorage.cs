using Lms.BuildingBlocks.Application;
using Microsoft.Extensions.Configuration;

namespace Lms.BuildingBlocks.Infrastructure;

/// <summary>Local-disk storage for one server. Swap this class for object storage when scaling out.</summary>
public sealed class LocalFileStorage(IConfiguration configuration) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(configuration["Storage:Path"]
        ?? throw new InvalidOperationException("Storage:Path is required."));

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var key = Guid.CreateVersion7().ToString("N");

        await using var file = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Stream Open(string storageKey) =>
        new FileStream(PathFor(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string storageKey) => File.Delete(PathFor(storageKey));

    private string PathFor(string storageKey)
    {
        // Keys are generated here, so anything else means tampered data.
        if (storageKey.Length != 32 || !storageKey.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return Path.Combine(_root, storageKey);
    }
}
