using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Enrollment.Infrastructure;

/// <summary>Enrollment records stay after erasure, as training records.</summary>
internal sealed class EnrollmentPersonalData(EnrollmentDbContext db) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cohorts = await db.Cohorts.AsNoTracking().Where(c => c.Enrollments.Any(e => e.TraineeId == userId)).ToListAsync(cancellationToken);

        return new PersonalDataSection("enrollments", cohorts.SelectMany(c => c.Enrollments.Where(e => e.TraineeId == userId).Select(e => new
        {
            Cohort = c.Name,
            c.StartsOn,
            c.EndsOn,
            Status = e.Status.ToString(),
            e.RequestedAt,
            e.DecidedAt,
            e.RejectionReason,
        })).ToList());
    }
}
