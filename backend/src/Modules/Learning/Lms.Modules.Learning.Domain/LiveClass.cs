using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public readonly record struct LiveClassId(Guid Value);

public enum LiveClassStatus { Scheduled, Live, Ended }

public sealed record LiveClassScheduled(Guid LiveClassId, Guid CohortId, string TitleAr, string TitleEn) : IDomainEvent;

public static class LiveClassErrors
{
    public static readonly Error NotFound = Error.NotFound("learning.live_not_found", "Live class was not found.");
    public static readonly Error InvalidDuration = Error.Validation("learning.live_invalid_duration", "A live class lasts between 15 and 480 minutes.");
    public static readonly Error InThePast = Error.Validation("learning.live_in_the_past", "Choose a time in the future.");
    public static readonly Error TooEarly = Error.Conflict("learning.live_too_early", "This class can start 15 minutes before its time.");
    public static readonly Error NotStarted = Error.Conflict("learning.live_not_started", "The instructor has not started this class yet.");
    public static readonly Error Ended = Error.Conflict("learning.live_ended", "This class has ended.");
    public static readonly Error AlreadyStarted = Error.Conflict("learning.live_already_started", "A class that has started cannot be cancelled.");
    public static readonly Error NotConfigured = Error.Conflict("learning.live_not_configured", "Live classes are not connected to a server yet.");
    public static readonly Error Unavailable = Error.Conflict("learning.live_unavailable", "The live class server did not respond. Try again.");
}

/// <summary>One scheduled class in a cohort, held on a BigBlueButton server.</summary>
public sealed class LiveClass : AggregateRoot<LiveClassId>
{
    public const int TitleMaxLength = 200;
    public const int MinMinutes = 15;
    public const int MaxMinutes = 480;
    public static readonly TimeSpan EarlyStart = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Overrun = TimeSpan.FromMinutes(30);

    private LiveClass()
    {
    }

    public Guid CohortId { get; private set; }
    public LocalizedText Title { get; private set; } = null!;
    public DateTimeOffset StartsAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public string MeetingId { get; private set; } = null!;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>The server ends a class on its own, so a class past its time counts as over even if nobody pressed End.</summary>
    public LiveClassStatus StatusAt(DateTimeOffset now)
    {
        if (EndedAt is not null || now > StartsAt.AddMinutes(DurationMinutes) + Overrun)
        {
            return LiveClassStatus.Ended;
        }

        return StartedAt is null ? LiveClassStatus.Scheduled : LiveClassStatus.Live;
    }

    public static Result<LiveClass> Schedule(Guid cohortId, LocalizedText title, DateTimeOffset startsAt, int durationMinutes, DateTimeOffset now)
    {
        if (durationMinutes is < MinMinutes or > MaxMinutes)
        {
            return LiveClassErrors.InvalidDuration;
        }

        if (startsAt <= now)
        {
            return LiveClassErrors.InThePast;
        }

        var id = Guid.CreateVersion7();
        var live = new LiveClass
        {
            Id = new LiveClassId(id),
            CohortId = cohortId,
            Title = title,
            StartsAt = startsAt,
            DurationMinutes = durationMinutes,
            MeetingId = id.ToString("N"),
        };

        live.Raise(new LiveClassScheduled(id, cohortId, title.Ar, title.En));
        return live;
    }

    public Result CheckCanStart(DateTimeOffset now)
    {
        if (StatusAt(now) == LiveClassStatus.Ended)
        {
            return LiveClassErrors.Ended;
        }

        return now < StartsAt - EarlyStart ? LiveClassErrors.TooEarly : Result.Success();
    }

    public void MarkStarted(DateTimeOffset now) => StartedAt ??= now;

    public void End(DateTimeOffset now) => EndedAt ??= now;

    public Result CheckCanJoin(DateTimeOffset now) => StatusAt(now) switch
    {
        LiveClassStatus.Live => Result.Success(),
        LiveClassStatus.Ended => LiveClassErrors.Ended,
        _ => LiveClassErrors.NotStarted,
    };

    public Result CheckCanCancel() => StartedAt is null ? Result.Success() : LiveClassErrors.AlreadyStarted;
}

public sealed class LiveAttendance
{
    private LiveAttendance()
    {
    }

    public Guid Id { get; private set; }
    public LiveClassId LiveClassId { get; private set; }
    public Guid TraineeId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    public static LiveAttendance Record(LiveClassId liveClassId, Guid traineeId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        LiveClassId = liveClassId,
        TraineeId = traineeId,
        JoinedAt = now,
    };
}
