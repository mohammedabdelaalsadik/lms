using Lms.Modules.Identity.Contracts;
using Lms.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Identity.Infrastructure;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<(IReadOnlyDictionary<string, int> ByRole, IReadOnlyList<DateTimeOffset> Joined)> GetStatsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var live = db.Users.AsNoTracking().Where(u => u.ErasedAt == null);
        var roles = await live.GroupBy(u => u.Role).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var joined = await live.Where(u => u.CreatedAt >= since).Select(u => u.CreatedAt).ToListAsync(cancellationToken);
        return (roles.ToDictionary(r => r.Key.ToString(), r => r.Count), joined);
    }

    public async Task<IReadOnlyList<UserInfo>> SearchAsync(string? term, int take, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var text = term.Trim();
            query = query.Where(u => u.FullName.Ar.Contains(text) || u.FullName.En.Contains(text));
        }

        var rows = await query
            .OrderBy(u => u.FullName.En)
            .Take(take)
            .Select(u => new { Id = u.Id.Value, u.FullName.Ar, u.FullName.En, u.NationalId.LastFour, u.Role, u.Email, u.Language })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new UserInfo(r.Id, r.Ar, r.En, $"******{r.LastFour}", r.Role.ToString(), r.Email, r.Language.ToString()))];
    }

    public async Task<IReadOnlyDictionary<Guid, UserInfo>> GetAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var userIds = ids.Select(id => new UserId(id)).ToList();

        var rows = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { Id = u.Id.Value, u.FullName.Ar, u.FullName.En, u.NationalId.LastFour, u.Role, u.Email, u.Language })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new UserInfo(r.Id, r.Ar, r.En, $"******{r.LastFour}", r.Role.ToString(), r.Email, r.Language.ToString()));
    }
}
