using Lms.SharedKernel;

namespace Lms.BuildingBlocks.Application;

/// <summary>Keeps uploaded files outside the web root under generated names.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    Stream Open(string storageKey);

    void Delete(string storageKey);
}

public static class FileErrors
{
    public static readonly Error TypeNotAllowed = Error.Validation("common.file_type_not_allowed", "This file type is not allowed.");
    public static readonly Error TooLarge = Error.Validation("common.file_too_large", "This file is too large.");
}

public static class FileRules
{
    public const long MaxSize = 50 * 1024 * 1024;

    /// <summary>The type served to browsers comes from this list, never from the uploader.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".mp4"] = "video/mp4",
    };

    public static string? ContentTypeFor(string fileName) =>
        AllowedTypes.GetValueOrDefault(Path.GetExtension(fileName));
}
