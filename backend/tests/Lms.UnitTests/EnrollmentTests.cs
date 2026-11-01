using Lms.Modules.Enrollment.Domain;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class CohortTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 12, 1);
    private static readonly LocalizedText Name = new("الدفعة", "Group");

    private static Cohort NewCohort(int capacity = 2, bool approval = false) =>
        Cohort.Create(Guid.NewGuid(), Name, " Riyadh ", Guid.NewGuid(), Start, Start.AddDays(5), capacity, approval, Now).Value;

    [Fact]
    public void Rejects_end_date_before_start() =>
        Assert.Equal(EnrollmentErrors.InvalidDates, Cohort.Create(Guid.NewGuid(), Name, "B", Guid.NewGuid(), Start, Start.AddDays(-1), 1, false, Now).Error);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Rejects_non_positive_capacity(int capacity) =>
        Assert.Equal(EnrollmentErrors.InvalidCapacity, Cohort.Create(Guid.NewGuid(), Name, "B", Guid.NewGuid(), Start, Start, capacity, false, Now).Error);

    [Fact]
    public void Direct_enrollment_takes_a_seat_and_raises_event()
    {
        var cohort = NewCohort();

        var result = cohort.Enroll(Guid.NewGuid(), Now);

        Assert.Equal(EnrollmentStatus.Enrolled, result.Value.Status);
        Assert.Equal(1, cohort.EnrolledCount);
        Assert.IsType<TraineeEnrolled>(Assert.Single(cohort.DomainEvents));
    }

    [Fact]
    public void Enforces_the_seat_limit()
    {
        var cohort = NewCohort(capacity: 1);
        cohort.Enroll(Guid.NewGuid(), Now);

        Assert.Equal(EnrollmentErrors.CohortFull, cohort.Enroll(Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void Rejects_a_second_active_enrollment()
    {
        var cohort = NewCohort();
        var trainee = Guid.NewGuid();
        cohort.Enroll(trainee, Now);

        Assert.Equal(EnrollmentErrors.AlreadyEnrolled, cohort.Enroll(trainee, Now).Error);
    }

    [Fact]
    public void Withdrawing_frees_the_seat_and_allows_enrolling_again()
    {
        var cohort = NewCohort(capacity: 1);
        var trainee = Guid.NewGuid();
        cohort.Enroll(trainee, Now);

        Assert.True(cohort.Withdraw(trainee, Now).IsSuccess);

        Assert.Equal(0, cohort.EnrolledCount);
        Assert.True(cohort.Enroll(trainee, Now).IsSuccess);
    }

    [Fact]
    public void Closes_enrollment_when_the_cohort_starts()
    {
        var onStartDay = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(EnrollmentErrors.EnrollmentPeriodOver, NewCohort().Enroll(Guid.NewGuid(), onStartDay).Error);
    }

    [Fact]
    public void Closed_cohort_refuses_enrollment()
    {
        var cohort = NewCohort();
        cohort.Close();

        Assert.Equal(EnrollmentErrors.CohortClosed, cohort.Enroll(Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void Pending_request_holds_no_seat_until_approved()
    {
        var cohort = NewCohort(approval: true);
        var enrollment = cohort.Enroll(Guid.NewGuid(), Now).Value;

        Assert.Equal(EnrollmentStatus.Pending, enrollment.Status);
        Assert.Equal(0, cohort.EnrolledCount);

        Assert.True(cohort.Approve(enrollment.Id, Now).IsSuccess);

        Assert.Equal(1, cohort.EnrolledCount);
        Assert.Equal(EnrollmentErrors.NotPending, cohort.Approve(enrollment.Id, Now).Error);
    }

    [Fact]
    public void Approval_cannot_exceed_capacity()
    {
        var cohort = NewCohort(capacity: 1, approval: true);
        var first = cohort.Enroll(Guid.NewGuid(), Now).Value;
        var second = cohort.Enroll(Guid.NewGuid(), Now).Value;
        cohort.Approve(first.Id, Now);

        Assert.Equal(EnrollmentErrors.CohortFull, cohort.Approve(second.Id, Now).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Rejection_needs_a_reason(string? reason)
    {
        var cohort = NewCohort(approval: true);
        var enrollment = cohort.Enroll(Guid.NewGuid(), Now).Value;

        Assert.Equal(EnrollmentErrors.ReasonRequired, cohort.Reject(enrollment.Id, reason, Now).Error);
        Assert.Equal(EnrollmentStatus.Pending, enrollment.Status);
    }
}

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() =>
        RefreshToken.Issue(new UserId(Guid.NewGuid()), "hash", Guid.NewGuid(), Now, Now.AddDays(14));

    [Fact]
    public void Is_active_until_expiry()
    {
        var token = NewToken();

        Assert.True(token.IsActive(Now));
        Assert.False(token.IsActive(Now.AddDays(14)));
    }

    [Fact]
    public void Rotation_ends_it_and_late_reuse_is_detected()
    {
        var token = NewToken();

        token.Rotate(Now);

        Assert.False(token.IsActive(Now));
        Assert.False(token.IsReuse(Now + RefreshToken.ReuseGrace));
        Assert.True(token.IsReuse(Now + RefreshToken.ReuseGrace + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Revoked_token_is_inactive_but_not_reuse()
    {
        var token = NewToken();

        token.Revoke(Now);

        Assert.False(token.IsActive(Now));
        Assert.False(token.IsReuse(Now.AddHours(1)));
    }
}
