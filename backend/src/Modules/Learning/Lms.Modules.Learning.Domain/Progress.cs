using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public readonly record struct CourseProgressId(Guid Value);

public readonly record struct AttendanceSessionId(Guid Value);

public enum AttendanceStatus { Present, Absent, Excused }

public sealed record CourseCompleted(Guid CohortId, Guid CourseId, Guid TraineeId) : IDomainEvent;

public sealed record AttendanceCorrected(
    Guid SessionId,
    Guid TraineeId,
    AttendanceStatus? OldStatus,
    AttendanceStatus NewStatus,
    Guid ChangedBy) : IDomainEvent;

public static class ProgressErrors
{
    public static readonly Error CohortNotFound = Error.NotFound("learning.cohort_not_found", "Cohort was not found.");
    public static readonly Error NotEnrolled = Error.Forbidden("learning.not_enrolled", "You are not enrolled in this cohort.");
    public static readonly Error NotStarted = Error.Conflict("learning.not_started", "This cohort has not started yet.");
    public static readonly Error ActivityLocked = Error.Conflict("learning.activity_locked", "Finish the earlier activities first.");
    public static readonly Error InvalidSessionDate = Error.Validation("learning.invalid_session_date", "The date must fall within the cohort and cannot be in the future.");
    public static readonly Error TraineeNotInCohort = Error.Validation("learning.trainee_not_in_cohort", "Attendance can only be taken for enrolled trainees.");
}

public sealed record AttendanceTally(int Present, int Excused)
{
    public static readonly AttendanceTally Empty = new(0, 0);
}

/// <summary>
/// What finishing a course requires. Attendance counts against planned sessions, less excused ones,
/// so the result never depends on how many sessions have been held so far.
/// </summary>
public sealed record CompletionRule(IReadOnlyCollection<ActivityId> RequiredActivities, int MinAttendancePercent, int PlannedSessions, int PassMarkPercent = 0)
{
    private bool HasRequirements => RequiredActivities.Count > 0 || MinAttendancePercent > 0 || PassMarkPercent > 0;

    public bool IsMetBy(IReadOnlySet<ActivityId> completed, AttendanceTally attendance, decimal? finalGrade = null) =>
        HasRequirements && RemainingActivities(completed) == 0 && AttendanceMet(attendance) && GradeMet(finalGrade);

    public bool GradeMet(decimal? finalGrade) => PassMarkPercent == 0 || finalGrade >= PassMarkPercent;

    public int RemainingActivities(IReadOnlySet<ActivityId> completed) =>
        RequiredActivities.Count(a => !completed.Contains(a));

    public bool AttendanceMet(AttendanceTally attendance)
    {
        if (MinAttendancePercent == 0)
        {
            return true;
        }

        var expected = PlannedSessions - attendance.Excused;
        return expected > 0 && attendance.Present * 100 >= MinAttendancePercent * expected;
    }

    public int AttendancePercent(AttendanceTally attendance)
    {
        var expected = PlannedSessions - attendance.Excused;
        return expected <= 0 ? 0 : Math.Min(100, attendance.Present * 100 / expected);
    }
}

public static class ScormResult
{
    /// <summary>SCORM 1.2 has one status. SCORM 2004 separates finishing from passing.</summary>
    public static bool IsComplete(IReadOnlyDictionary<string, string> values) =>
        values.GetValueOrDefault("cmi.core.lesson_status") is "completed" or "passed"
        || values.GetValueOrDefault("cmi.completion_status") == "completed"
        || values.GetValueOrDefault("cmi.success_status") == "passed";
}

/// <summary>A trainee's saved place in a SCORM package.</summary>
public sealed class ScormAttempt
{
    private ScormAttempt()
    {
    }

    public Guid Id { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public Guid CohortId { get; private set; }
    public Guid TraineeId { get; private set; }
    public string Data { get; private set; } = "{}";
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static ScormAttempt Start(ActivityId activityId, Guid cohortId, Guid traineeId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ActivityId = activityId,
        CohortId = cohortId,
        TraineeId = traineeId,
        UpdatedAt = now,
    };

    public void Save(string data, bool complete, DateTimeOffset now)
    {
        Data = data;
        UpdatedAt = now;

        if (complete)
        {
            CompletedAt ??= now;
        }
    }
}

public sealed class ActivityCompletion
{
    private ActivityCompletion()
    {
    }

    public ActivityId ActivityId { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    internal static ActivityCompletion Create(ActivityId activityId, DateTimeOffset now) => new()
    {
        ActivityId = activityId,
        CompletedAt = now,
    };
}

/// <summary>One trainee's progress through a course in one cohort.</summary>
public sealed class CourseProgress : AggregateRoot<CourseProgressId>
{
    private readonly List<ActivityCompletion> _completions = [];

    private CourseProgress()
    {
    }

    public Guid CohortId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid TraineeId { get; private set; }
    public Guid? LastActivityId { get; private set; }
    public decimal? FinalGradePercent { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<ActivityCompletion> Completions => _completions;

    public static CourseProgress Start(Guid cohortId, Guid courseId, Guid traineeId) => new()
    {
        Id = new CourseProgressId(Guid.CreateVersion7()),
        CohortId = cohortId,
        CourseId = courseId,
        TraineeId = traineeId,
    };

    public IReadOnlySet<ActivityId> CompletedActivities() => _completions.Select(c => c.ActivityId).ToHashSet();

    /// <summary>In a sequential course an activity opens once everything before it is done.</summary>
    public bool CanOpen(ActivityId activity, IReadOnlyList<ActivityId> order, bool isSequential)
    {
        if (!isSequential)
        {
            return true;
        }

        var completed = CompletedActivities();
        return order.TakeWhile(a => a != activity).All(completed.Contains);
    }

    public Result CompleteActivity(ActivityId activity, IReadOnlyList<ActivityId> order, bool isSequential, DateTimeOffset now)
    {
        if (!CanOpen(activity, order, isSequential))
        {
            return ProgressErrors.ActivityLocked;
        }

        if (_completions.TrueForAll(c => c.ActivityId != activity))
        {
            _completions.Add(ActivityCompletion.Create(activity, now));
        }

        LastActivityId = activity.Value;
        return Result.Success();
    }

    public void RecordFinalGrade(decimal percent) => FinalGradePercent = percent;

    /// <summary>Records completion once the rule is met. A recorded completion is never undone here.</summary>
    public void EvaluateCompletion(CompletionRule rule, AttendanceTally attendance, DateTimeOffset now)
    {
        if (CompletedAt is null && rule.IsMetBy(CompletedActivities(), attendance, FinalGradePercent))
        {
            CompletedAt = now;
            Raise(new CourseCompleted(CohortId, CourseId, TraineeId));
        }
    }
}

public sealed class AttendanceRecord
{
    private AttendanceRecord()
    {
    }

    public Guid TraineeId { get; private set; }
    public AttendanceStatus Status { get; private set; }

    internal static AttendanceRecord Create(Guid traineeId, AttendanceStatus status) => new()
    {
        TraineeId = traineeId,
        Status = status,
    };

    internal void Change(AttendanceStatus status) => Status = status;
}

/// <summary>Attendance for one cohort on one date. Every later change is kept as a domain event.</summary>
public sealed class AttendanceSession : AggregateRoot<AttendanceSessionId>
{
    private readonly List<AttendanceRecord> _records = [];

    private AttendanceSession()
    {
    }

    public Guid CohortId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid RecordedBy { get; private set; }

    public IReadOnlyCollection<AttendanceRecord> Records => _records;

    public static AttendanceSession Take(
        Guid cohortId,
        DateOnly date,
        Guid recordedBy,
        IReadOnlyDictionary<Guid, AttendanceStatus> entries)
    {
        var session = new AttendanceSession
        {
            Id = new AttendanceSessionId(Guid.CreateVersion7()),
            CohortId = cohortId,
            Date = date,
            RecordedBy = recordedBy,
        };

        session._records.AddRange(entries.Select(e => AttendanceRecord.Create(e.Key, e.Value)));
        return session;
    }

    public void Correct(IReadOnlyDictionary<Guid, AttendanceStatus> entries, Guid changedBy)
    {
        foreach (var (traineeId, status) in entries)
        {
            var record = _records.Find(r => r.TraineeId == traineeId);

            if (record is null)
            {
                _records.Add(AttendanceRecord.Create(traineeId, status));
                Raise(new AttendanceCorrected(Id.Value, traineeId, null, status, changedBy));
            }
            else if (record.Status != status)
            {
                Raise(new AttendanceCorrected(Id.Value, traineeId, record.Status, status, changedBy));
                record.Change(status);
            }
        }
    }
}
