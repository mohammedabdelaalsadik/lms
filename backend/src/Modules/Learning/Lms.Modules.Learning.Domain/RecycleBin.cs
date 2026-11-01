using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public enum BinItemKind { Section, Activity }

public sealed record ActivitySnapshot(
    Guid Id,
    string TitleAr,
    string TitleEn,
    ActivityKind Kind,
    bool IsRequired,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    string? FileKey,
    string? FileName,
    string? FileContentType,
    long? FileSize,
    string? ScormEntry,
    string? FormJson = null,
    string? StructureJson = null);

public sealed record SectionSnapshot(Guid Id, string TitleAr, string TitleEn, IReadOnlyList<ActivitySnapshot> Activities);

public static class BinErrors
{
    public static readonly Error NotFound = Error.NotFound("learning.bin_item_not_found", "This item is no longer in the recycle bin.");
    public static readonly Error NoSection = Error.Conflict("learning.bin_no_section", "Add a section first, then restore this activity.");
}

/// <summary>
/// Removed content, held for a while. Its files stay in storage until the item is purged. Restoring
/// keeps the original ids, so trainees' completions of the activity still count.
/// </summary>
public sealed class BinItem
{
    public const int RetentionDays = 30;

    private BinItem()
    {
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public BinItemKind Kind { get; private set; }
    public string TitleAr { get; private set; } = null!;
    public string TitleEn { get; private set; } = null!;
    public Guid? SectionId { get; private set; }
    public string Payload { get; private set; } = null!;
    public Guid RemovedBy { get; private set; }
    public DateTimeOffset RemovedAt { get; private set; }

    public DateTimeOffset ExpiresAt => RemovedAt.AddDays(RetentionDays);

    public static BinItem Create(
        Guid courseId,
        BinItemKind kind,
        string titleAr,
        string titleEn,
        Guid? sectionId,
        string payload,
        Guid removedBy,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            CourseId = courseId,
            Kind = kind,
            TitleAr = titleAr,
            TitleEn = titleEn,
            SectionId = sectionId,
            Payload = payload,
            RemovedBy = removedBy,
            RemovedAt = now,
        };
}
