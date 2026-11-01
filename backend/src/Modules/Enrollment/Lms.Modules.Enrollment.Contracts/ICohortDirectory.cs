namespace Lms.Modules.Enrollment.Contracts;

public sealed record GroupInfo(Guid Id, string Name);

public sealed record CohortInfo(Guid Id, Guid CourseId, Guid InstructorId, string NameAr, string NameEn, DateOnly StartsOn, DateOnly EndsOn);

/// <summary>What other modules may ask the Enrollment module. Only active enrollments count.</summary>
public interface ICohortDirectory
{
    Task<CohortInfo?> FindAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetEnrolledTraineesAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<bool> IsEnrolledAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    Task<bool> TeachesCourseAsync(Guid instructorId, Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CohortInfo>> GetEnrolledCohortsAsync(Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> GetEnrollmentCountsAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken);

    /// <summary>When each active enrollment in these cohorts was requested, from a date on.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetEnrollmentDatesAsync(IReadOnlyCollection<Guid> cohortIds, DateTimeOffset since, CancellationToken cancellationToken);

    Task<IReadOnlyList<GroupInfo>> GetGroupsAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<Guid?> GetGroupOfAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    /// <summary>All cohorts, or only those of one instructor.</summary>
    Task<IReadOnlyList<CohortInfo>> GetCohortsAsync(Guid? instructorId, CancellationToken cancellationToken);

    /// <summary>Instructors and classmates of a trainee, or the trainees of an instructor.</summary>
    Task<IReadOnlyCollection<Guid>> GetContactIdsAsync(Guid userId, CancellationToken cancellationToken);
}
