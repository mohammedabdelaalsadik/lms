using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Certification.Infrastructure;

internal sealed class BadgeRepository(CertificationDbContext db) : IBadgeRepository
{
    public Task<Badge?> FindAsync(BadgeId id, CancellationToken cancellationToken) =>
        db.Badges.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Badge>> ListAsync(CancellationToken cancellationToken) =>
        await db.Badges.AsNoTracking().OrderBy(b => b.Name.En).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Badge>> ListLiveForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.Badges.AsNoTracking().Where(b => b.CourseId == courseId && b.RetiredAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(Badge Badge, BadgeAward Award)>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken)
    {
        var rows = await (from award in db.BadgeAwards.AsNoTracking()
                          join badge in db.Badges.AsNoTracking() on award.BadgeId equals badge.Id
                          where award.TraineeId == traineeId
                          orderby award.AwardedAt descending
                          select new { badge, award }).ToListAsync(cancellationToken);

        return [.. rows.Select(r => (r.badge, r.award))];
    }

    public Task<bool> HasAwardAsync(BadgeId id, Guid traineeId, CancellationToken cancellationToken) =>
        db.BadgeAwards.AnyAsync(a => a.BadgeId == id && a.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlyDictionary<BadgeId, int>> CountAwardsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.BadgeAwards.AsNoTracking().GroupBy(a => a.BadgeId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.Count);
    }

    public Task RemoveAwardsAsync(Guid courseId, Guid traineeId, CancellationToken cancellationToken) =>
        db.BadgeAwards.Where(a => a.CourseId == courseId && a.TraineeId == traineeId).ExecuteDeleteAsync(cancellationToken);

    public void Add(Badge badge) => db.Badges.Add(badge);

    public void Add(BadgeAward award) => db.BadgeAwards.Add(award);
}
