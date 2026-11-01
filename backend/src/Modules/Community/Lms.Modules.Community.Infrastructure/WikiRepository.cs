using Lms.Modules.Community.Application;
using Lms.Modules.Community.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Community.Infrastructure;

internal sealed class WikiRepository(CommunityDbContext db) : IWikiRepository
{
    public Task<WikiPage?> FindAsync(WikiPageId id, CancellationToken cancellationToken) =>
        db.WikiPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WikiPage>> ListAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.WikiPages.AsNoTracking().Where(p => p.CohortId == cohortId && p.RemovedAt == null).OrderBy(p => p.Title).ToListAsync(cancellationToken);

    public Task<bool> TitleTakenAsync(Guid cohortId, string title, WikiPageId? except, CancellationToken cancellationToken) =>
        db.WikiPages.AnyAsync(p => p.CohortId == cohortId && p.Title == title && p.RemovedAt == null && p.Id != except, cancellationToken);

    public async Task<IReadOnlyList<WikiRevision>> ListRevisionsAsync(WikiPageId id, CancellationToken cancellationToken) =>
        await db.WikiRevisions.AsNoTracking().Where(r => r.PageId == id).OrderByDescending(r => r.Version).ToListAsync(cancellationToken);

    public void Add(WikiPage page) => db.WikiPages.Add(page);

    public void Add(WikiRevision revision) => db.WikiRevisions.Add(revision);
}
