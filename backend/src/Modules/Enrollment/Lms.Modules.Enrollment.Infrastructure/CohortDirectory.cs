using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Enrollment.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Enrollment.Infrastructure;

internal sealed class CohortDirectory(EnrollmentDbContext db) : ICohortDirectory
{
    public async Task<CohortInfo?> FindAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var id = new CohortId(cohortId);

        return await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CohortInfo(c.Id.Value, c.CourseId, c.InstructorId, c.Name.Ar, c.Name.En, c.StartsOn, c.EndsOn))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetEnrolledTraineesAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var id = new CohortId(cohortId);

        return await db.Cohorts.AsNoTracking()
            .Where(c => c.Id == id)
            .SelectMany(c => c.Enrollments)
            .Where(e => e.Status == EnrollmentStatus.Enrolled)
            .Select(e => e.TraineeId)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsEnrolledAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken)
    {
        var id = new CohortId(cohortId);

        return db.Cohorts.AsNoTracking()
            .Where(c => c.Id == id)
            .SelectMany(c => c.Enrollments)
            .AnyAsync(e => e.TraineeId == traineeId && e.Status == EnrollmentStatus.Enrolled, cancellationToken);
    }

    public async Task<IReadOnlyList<GroupInfo>> GetGroupsAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.Groups.AsNoTracking()
            .Where(g => g.CohortId == cohortId)
            .OrderBy(g => g.Name)
            .Select(g => new GroupInfo(g.Id.Value, g.Name))
            .ToListAsync(cancellationToken);

    public async Task<Guid?> GetGroupOfAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken) =>
        await db.Groups.AsNoTracking()
            .Where(g => g.CohortId == cohortId && g.Members.Any(m => m.TraineeId == traineeId))
            .Select(g => (Guid?)g.Id.Value)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> GetEnrollmentDatesAsync(IReadOnlyCollection<Guid> cohortIds, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var ids = cohortIds.Select(id => new CohortId(id)).ToList();

        return await db.Cohorts.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .SelectMany(c => c.Enrollments)
            .Where(e => e.Status == EnrollmentStatus.Enrolled && e.RequestedAt >= since)
            .Select(e => e.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetEnrollmentCountsAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken)
    {
        var ids = cohortIds.Select(id => new CohortId(id)).ToList();

        var rows = await db.Cohorts.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, Count = c.Enrollments.Count(e => e.Status == EnrollmentStatus.Enrolled) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id.Value, r => r.Count);
    }

    public async Task<IReadOnlyList<CohortInfo>> GetCohortsAsync(Guid? instructorId, CancellationToken cancellationToken) =>
        await db.Cohorts.AsNoTracking()
            .Where(c => instructorId == null || c.InstructorId == instructorId)
            .OrderByDescending(c => c.StartsOn)
            .Select(c => new CohortInfo(c.Id.Value, c.CourseId, c.InstructorId, c.Name.Ar, c.Name.En, c.StartsOn, c.EndsOn))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> GetContactIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Cohorts.AsNoTracking()
            .Where(c => c.InstructorId == userId || c.Enrollments.Any(e => e.TraineeId == userId && e.Status == EnrollmentStatus.Enrolled))
            .Select(c => new
            {
                c.InstructorId,
                Trainees = c.Enrollments.Where(e => e.Status == EnrollmentStatus.Enrolled).Select(e => e.TraineeId).ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows.SelectMany(r => r.Trainees.Append(r.InstructorId)).Where(id => id != userId).ToHashSet();
    }

    public Task<bool> TeachesCourseAsync(Guid instructorId, Guid courseId, CancellationToken cancellationToken) =>
        db.Cohorts.AsNoTracking().AnyAsync(c => c.InstructorId == instructorId && c.CourseId == courseId, cancellationToken);

    public async Task<IReadOnlyList<CohortInfo>> GetEnrolledCohortsAsync(Guid traineeId, CancellationToken cancellationToken) =>
        await db.Cohorts.AsNoTracking()
            .Where(c => c.Enrollments.Any(e => e.TraineeId == traineeId && e.Status == EnrollmentStatus.Enrolled))
            .OrderBy(c => c.StartsOn)
            .Select(c => new CohortInfo(c.Id.Value, c.CourseId, c.InstructorId, c.Name.Ar, c.Name.En, c.StartsOn, c.EndsOn))
            .ToListAsync(cancellationToken);
}
