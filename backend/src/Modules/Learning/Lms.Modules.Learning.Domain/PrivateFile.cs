using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public static class PrivateFileErrors
{
    public static readonly Error NotFound = Error.NotFound("learning.private_file_not_found", "File was not found.");
    public static readonly Error QuotaExceeded = Error.Conflict("learning.private_quota_exceeded", "This would go over your storage limit.");
}

/// <summary>A person's own file. Only its owner can ever reach it, including staff and administrators.</summary>
public sealed class PrivateFile
{
    public const long QuotaBytes = 200L * 1024 * 1024;

    private PrivateFile()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long Size { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    public static bool Fits(long used, long size) => used + size <= QuotaBytes;

    public static PrivateFile Create(Guid ownerId, FileAsset file, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = ownerId,
        StorageKey = file.StorageKey,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Size = file.Size,
        UploadedAt = now,
    };
}
