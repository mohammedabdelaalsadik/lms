using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Certification.Infrastructure;

/// <summary>Certificates stay after erasure: they are official records that anyone can verify, and they carry a name snapshot.</summary>
internal sealed class CertificationPersonalData(CertificationDbContext db) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var certificates = await db.Certificates.AsNoTracking().Where(c => c.TraineeId == userId).ToListAsync(cancellationToken);
        var badges = await (from award in db.BadgeAwards.AsNoTracking()
                            join badge in db.Badges.AsNoTracking() on award.BadgeId equals badge.Id
                            where award.TraineeId == userId
                            select new { badge.Name, award.AwardedAt }).ToListAsync(cancellationToken);
        var plans = await (from assignment in db.PlanAssignments.AsNoTracking()
                           join plan in db.LearningPlans.AsNoTracking() on assignment.PlanId equals plan.Id
                           where assignment.TraineeId == userId
                           select new { plan.Name, assignment.DueOn, assignment.AssignedAt }).ToListAsync(cancellationToken);
        var signOffs = await (from signOff in db.CompetencySignOffs.AsNoTracking()
                              join competency in db.Competencies.AsNoTracking() on signOff.CompetencyId equals competency.Id
                              where signOff.TraineeId == userId
                              select new { competency.Code, signOff.SignedAt }).ToListAsync(cancellationToken);

        return new PersonalDataSection("certification", new
        {
            Certificates = certificates.Select(c => new { c.Code, c.CourseCode, CourseTitle = new { c.CourseTitleAr, c.CourseTitleEn }, Status = c.Status.ToString(), c.CompletedOn, c.IssuedAt }),
            Badges = badges,
            LearningPlans = plans,
            CompetencySignOffs = signOffs,
        });
    }
}
