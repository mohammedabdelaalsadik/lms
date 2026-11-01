using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Domain;

namespace Lms.Modules.Learning.Application;

public interface ILearningUnitOfWork : IUnitOfWork;

public interface ICourseContentRepository
{
    Task<CourseContent?> FindByCourseAsync(Guid courseId, CancellationToken cancellationToken);

    Task<CourseContent?> FindByActivityAsync(ActivityId activityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseContent>> ListByCoursesAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken);

    void Add(CourseContent content);
}

public interface ICourseProgressRepository
{
    Task<CourseProgress?> FindAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseProgress>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseProgress>> ListByTraineeAsync(Guid traineeId, CancellationToken cancellationToken);

    void Add(CourseProgress progress);
}

public interface IAttendanceRepository
{
    Task<AttendanceSession?> FindAsync(Guid cohortId, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttendanceSession>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, AttendanceTally>> TallyAsync(Guid cohortId, CancellationToken cancellationToken);

    void Add(AttendanceSession session);
}

/// <summary>Who may edit a course or run a cohort. Instructors are limited to what they teach.</summary>
public sealed class LearningAccess(ICurrentUser user, ICohortDirectory cohorts)
{
    public bool IsManager => user.Role is LmsRoles.Admin or LmsRoles.Coordinator;

    public async Task<bool> CanEditCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        IsManager
        || (user.Role == LmsRoles.Instructor
            && user.Id is { } id
            && await cohorts.TeachesCourseAsync(id, courseId, cancellationToken));

    public bool CanRunCohort(CohortInfo cohort) => IsManager || cohort.InstructorId == user.Id;
}
