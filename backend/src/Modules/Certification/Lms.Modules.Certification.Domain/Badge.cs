using Lms.SharedKernel;

namespace Lms.Modules.Certification.Domain;

public readonly record struct BadgeId(Guid Value);

public static class BadgeErrors
{
    public static readonly Error NotFound = Error.NotFound("certification.badge_not_found", "Badge was not found.");
    public static readonly Error InvalidIcon = Error.Validation("certification.badge_invalid_icon", "Choose one of the listed icons.");
}

/// <summary>Earned by completing a course. Removed if that course's certificate is revoked.</summary>
public sealed class Badge : AggregateRoot<BadgeId>
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public static readonly string[] Icons = ["military_tech", "workspace_premium", "emoji_events", "verified", "star", "school"];

    private Badge()
    {
    }

    public Guid CourseId { get; private set; }
    public LocalizedText Name { get; private set; } = null!;
    public LocalizedText Description { get; private set; } = null!;
    public string Icon { get; private set; } = null!;
    public DateTimeOffset? RetiredAt { get; private set; }

    public static Result<Badge> Create(Guid courseId, LocalizedText name, LocalizedText description, string icon)
    {
        if (!Icons.Contains(icon))
        {
            return BadgeErrors.InvalidIcon;
        }

        return new Badge { Id = new BadgeId(Guid.CreateVersion7()), CourseId = courseId, Name = name, Description = description, Icon = icon };
    }

    /// <summary>Stops new awards. People who earned it keep it.</summary>
    public void Retire(DateTimeOffset now) => RetiredAt ??= now;
}

public sealed class BadgeAward
{
    private BadgeAward()
    {
    }

    public Guid Id { get; private set; }
    public BadgeId BadgeId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid TraineeId { get; private set; }
    public DateTimeOffset AwardedAt { get; private set; }

    public static BadgeAward Create(Badge badge, Guid traineeId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        BadgeId = badge.Id,
        CourseId = badge.CourseId,
        TraineeId = traineeId,
        AwardedAt = now,
    };
}
