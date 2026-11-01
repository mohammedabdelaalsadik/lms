using Lms.SharedKernel;

namespace Lms.Modules.Catalog.Domain;

public readonly record struct CourseId(Guid Value);

public enum CourseStatus { Draft, Published, Archived }

public sealed record CoursePublished(Guid CourseId, string Code) : IDomainEvent;

public sealed record CourseArchived(Guid CourseId, string Code) : IDomainEvent;

public static class CourseErrors
{
    public static readonly Error NotFound = Error.NotFound("catalog.course.not_found", "Course was not found.");
    public static readonly Error CodeTaken = Error.Conflict("catalog.course.code_taken", "This course code is already used.");
    public static readonly Error InvalidDuration = Error.Validation("catalog.course.invalid_duration", "Duration must be greater than zero.");
    public static readonly Error OnlyDraftCanChange = Error.Conflict("catalog.course.only_draft_can_change", "Only a draft course can be changed.");
    public static readonly Error OnlyDraftCanPublish = Error.Conflict("catalog.course.only_draft_can_publish", "Only a draft course can be published.");
    public static readonly Error OnlyPublishedCanArchive = Error.Conflict("catalog.course.only_published_can_archive", "Only a published course can be archived.");
}

/// <summary>A course moves one way only: Draft, then Published, then Archived.</summary>
public sealed class Course : AggregateRoot<CourseId>
{
    public const int CodeMaxLength = 20;
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private Course()
    {
    }

    public string Code { get; private set; } = null!;
    public LocalizedText Title { get; private set; } = null!;
    public LocalizedText Description { get; private set; } = null!;
    public int DurationHours { get; private set; }
    public CourseStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid? CategoryId { get; private set; }

    public void MoveTo(Guid? categoryId) => CategoryId = categoryId;

    public static Result<Course> Create(
        string code,
        LocalizedText title,
        LocalizedText description,
        int durationHours,
        DateTimeOffset now,
        Guid? id = null)
    {
        if (durationHours <= 0)
        {
            return CourseErrors.InvalidDuration;
        }

        return new Course
        {
            Id = new CourseId(id ?? Guid.CreateVersion7()),
            Code = NormalizeCode(code),
            Title = title,
            Description = description,
            DurationHours = durationHours,
            Status = CourseStatus.Draft,
            CreatedAt = now,
        };
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public Result UpdateDetails(LocalizedText title, LocalizedText description, int durationHours)
    {
        if (Status != CourseStatus.Draft)
        {
            return CourseErrors.OnlyDraftCanChange;
        }

        if (durationHours <= 0)
        {
            return CourseErrors.InvalidDuration;
        }

        Title = title;
        Description = description;
        DurationHours = durationHours;
        return Result.Success();
    }

    public Result Publish(DateTimeOffset now)
    {
        if (Status != CourseStatus.Draft)
        {
            return CourseErrors.OnlyDraftCanPublish;
        }

        Status = CourseStatus.Published;
        PublishedAt = now;
        Raise(new CoursePublished(Id.Value, Code));
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status != CourseStatus.Published)
        {
            return CourseErrors.OnlyPublishedCanArchive;
        }

        Status = CourseStatus.Archived;
        Raise(new CourseArchived(Id.Value, Code));
        return Result.Success();
    }
}
