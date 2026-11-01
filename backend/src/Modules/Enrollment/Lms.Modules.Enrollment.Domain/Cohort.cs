using Lms.SharedKernel;

namespace Lms.Modules.Enrollment.Domain;

public readonly record struct CohortId(Guid Value);

public readonly record struct EnrollmentId(Guid Value);

public enum CohortStatus { Open, Closed }

public enum EnrollmentStatus { Pending, Enrolled, Rejected, Withdrawn }

public sealed record EnrollmentRequested(Guid CohortId, Guid EnrollmentId, Guid TraineeId) : IDomainEvent;

public sealed record TraineeEnrolled(Guid CohortId, Guid CourseId, Guid EnrollmentId, Guid TraineeId) : IDomainEvent;

public sealed record EnrollmentRejected(Guid CohortId, Guid CourseId, Guid EnrollmentId, Guid TraineeId, string Reason) : IDomainEvent;

public sealed record TraineeWithdrew(Guid CohortId, Guid EnrollmentId, Guid TraineeId) : IDomainEvent;

public static class EnrollmentErrors
{
    public static readonly Error GroupNotFound = Error.NotFound("enrollment.group_not_found", "Group was not found.");
    public static readonly Error GroupNameTaken = Error.Conflict("enrollment.group_name_taken", "This cohort already has a group with that name.");
    public static readonly Error CohortNotFound = Error.NotFound("enrollment.cohort.not_found", "Cohort was not found.");
    public static readonly Error EnrollmentNotFound = Error.NotFound("enrollment.not_found", "Enrollment was not found.");
    public static readonly Error CourseNotPublished = Error.Conflict("enrollment.course_not_published", "Cohorts can only be opened for a published course.");
    public static readonly Error InvalidInstructor = Error.Validation("enrollment.invalid_instructor", "The selected user is not an instructor.");
    public static readonly Error InvalidDates = Error.Validation("enrollment.invalid_dates", "The end date cannot be before the start date.");
    public static readonly Error InvalidCapacity = Error.Validation("enrollment.invalid_capacity", "Seats must be greater than zero.");
    public static readonly Error CohortClosed = Error.Conflict("enrollment.cohort_closed", "This cohort is closed.");
    public static readonly Error EnrollmentPeriodOver = Error.Conflict("enrollment.period_over", "Enrollment ended when the cohort started.");
    public static readonly Error CohortFull = Error.Conflict("enrollment.cohort_full", "This cohort has no seats left.");
    public static readonly Error AlreadyEnrolled = Error.Conflict("enrollment.already_enrolled", "You already have an enrollment in this cohort.");
    public static readonly Error NotPending = Error.Conflict("enrollment.not_pending", "This request has already been decided.");
    public static readonly Error ReasonRequired = Error.Validation("enrollment.reason_required", "A reason is required.");
    public static readonly Error NothingToWithdraw = Error.Conflict("enrollment.nothing_to_withdraw", "There is no active enrollment to withdraw.");
    public static readonly Error Forbidden = Error.Forbidden("enrollment.forbidden", "You cannot view this cohort.");
}

/// <summary>One trainee's place, or request for a place, in a cohort.</summary>
public sealed class CohortEnrollment : Entity<EnrollmentId>
{
    public const int ReasonMaxLength = 500;

    private CohortEnrollment()
    {
    }

    public Guid TraineeId { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public bool IsActive => Status is EnrollmentStatus.Pending or EnrollmentStatus.Enrolled;

    internal static CohortEnrollment Create(Guid traineeId, EnrollmentStatus status, DateTimeOffset now) => new()
    {
        Id = new EnrollmentId(Guid.CreateVersion7()),
        TraineeId = traineeId,
        Status = status,
        RequestedAt = now,
        DecidedAt = status == EnrollmentStatus.Enrolled ? now : null,
    };

    internal void Approve(DateTimeOffset now)
    {
        Status = EnrollmentStatus.Enrolled;
        DecidedAt = now;
    }

    internal void Reject(string reason, DateTimeOffset now)
    {
        Status = EnrollmentStatus.Rejected;
        RejectionReason = reason;
        DecidedAt = now;
    }

    internal void Withdraw(DateTimeOffset now)
    {
        Status = EnrollmentStatus.Withdrawn;
        DecidedAt = now;
    }
}

/// <summary>
/// A scheduled run of a course. It owns its enrollments because the seat limit spans all of them.
/// <see cref="EnrolledCount"/> lives on this row so two simultaneous enrollments collide on its row
/// version, and one of them fails rather than overselling a seat.
/// </summary>
public sealed class Cohort : AggregateRoot<CohortId>
{
    public const int NameMaxLength = 150;
    public const int BranchMaxLength = 150;

    private readonly List<CohortEnrollment> _enrollments = [];

    private Cohort()
    {
    }

    public Guid CourseId { get; private set; }
    public LocalizedText Name { get; private set; } = null!;
    public string Branch { get; private set; } = null!;
    public Guid InstructorId { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public int Capacity { get; private set; }
    public int EnrolledCount { get; private set; }
    public bool RequiresApproval { get; private set; }
    public CohortStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<CohortEnrollment> Enrollments => _enrollments;

    public static Result<Cohort> Create(
        Guid courseId,
        LocalizedText name,
        string branch,
        Guid instructorId,
        DateOnly startsOn,
        DateOnly endsOn,
        int capacity,
        bool requiresApproval,
        DateTimeOffset now)
    {
        if (endsOn < startsOn)
        {
            return EnrollmentErrors.InvalidDates;
        }

        if (capacity <= 0)
        {
            return EnrollmentErrors.InvalidCapacity;
        }

        return new Cohort
        {
            Id = new CohortId(Guid.CreateVersion7()),
            CourseId = courseId,
            Name = name,
            Branch = branch.Trim(),
            InstructorId = instructorId,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Capacity = capacity,
            RequiresApproval = requiresApproval,
            Status = CohortStatus.Open,
            CreatedAt = now,
        };
    }

    public Result<CohortEnrollment> Enroll(Guid traineeId, DateTimeOffset now)
    {
        if (Status != CohortStatus.Open)
        {
            return EnrollmentErrors.CohortClosed;
        }

        if (DateOnly.FromDateTime(now.UtcDateTime) >= StartsOn)
        {
            return EnrollmentErrors.EnrollmentPeriodOver;
        }

        if (_enrollments.Exists(e => e.TraineeId == traineeId && e.IsActive))
        {
            return EnrollmentErrors.AlreadyEnrolled;
        }

        if (EnrolledCount >= Capacity)
        {
            return EnrollmentErrors.CohortFull;
        }

        var status = RequiresApproval ? EnrollmentStatus.Pending : EnrollmentStatus.Enrolled;
        var enrollment = CohortEnrollment.Create(traineeId, status, now);
        _enrollments.Add(enrollment);

        if (status == EnrollmentStatus.Enrolled)
        {
            TakeSeat(enrollment);
        }
        else
        {
            Raise(new EnrollmentRequested(Id.Value, enrollment.Id.Value, traineeId));
        }

        return enrollment;
    }

    public Result Approve(EnrollmentId enrollmentId, DateTimeOffset now)
    {
        var enrollment = _enrollments.Find(e => e.Id == enrollmentId);
        if (enrollment is null)
        {
            return EnrollmentErrors.EnrollmentNotFound;
        }

        if (enrollment.Status != EnrollmentStatus.Pending)
        {
            return EnrollmentErrors.NotPending;
        }

        if (EnrolledCount >= Capacity)
        {
            return EnrollmentErrors.CohortFull;
        }

        enrollment.Approve(now);
        TakeSeat(enrollment);
        return Result.Success();
    }

    public Result Reject(EnrollmentId enrollmentId, string? reason, DateTimeOffset now)
    {
        var enrollment = _enrollments.Find(e => e.Id == enrollmentId);
        if (enrollment is null)
        {
            return EnrollmentErrors.EnrollmentNotFound;
        }

        if (enrollment.Status != EnrollmentStatus.Pending)
        {
            return EnrollmentErrors.NotPending;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return EnrollmentErrors.ReasonRequired;
        }

        enrollment.Reject(reason.Trim(), now);
        Raise(new EnrollmentRejected(Id.Value, CourseId, enrollment.Id.Value, enrollment.TraineeId, enrollment.RejectionReason!));
        return Result.Success();
    }

    public Result Withdraw(Guid traineeId, DateTimeOffset now)
    {
        var enrollment = _enrollments.Find(e => e.TraineeId == traineeId && e.IsActive);
        if (enrollment is null)
        {
            return EnrollmentErrors.NothingToWithdraw;
        }

        if (enrollment.Status == EnrollmentStatus.Enrolled)
        {
            EnrolledCount--;
        }

        enrollment.Withdraw(now);
        Raise(new TraineeWithdrew(Id.Value, enrollment.Id.Value, traineeId));
        return Result.Success();
    }

    public Result Close()
    {
        if (Status == CohortStatus.Closed)
        {
            return EnrollmentErrors.CohortClosed;
        }

        Status = CohortStatus.Closed;
        return Result.Success();
    }

    private void TakeSeat(CohortEnrollment enrollment)
    {
        EnrolledCount++;
        Raise(new TraineeEnrolled(Id.Value, CourseId, enrollment.Id.Value, enrollment.TraineeId));
    }
}
