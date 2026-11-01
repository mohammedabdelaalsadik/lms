using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Community.Infrastructure;

/// <summary>Only what the person wrote. Replies from others stay readable, and other people's messages are never exported.</summary>
internal sealed class CommunityPersonalData(CommunityDbContext db, TimeProvider clock) : IPersonalDataSource
{
    private const string ErasedText = "—";

    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var topics = await db.Topics.AsNoTracking().Where(t => t.AuthorId == userId && t.RemovedAt == null).Select(t => new { t.Title, t.Body, t.CreatedAt }).ToListAsync(cancellationToken);
        var posts = await db.Posts.AsNoTracking().Where(p => p.AuthorId == userId && p.RemovedAt == null).Select(p => new { p.Body, p.CreatedAt }).ToListAsync(cancellationToken);
        var messages = await db.Messages.AsNoTracking().Where(m => m.SenderId == userId).Select(m => new { m.Body, m.SentAt }).ToListAsync(cancellationToken);
        var edits = await (from revision in db.WikiRevisions.AsNoTracking()
                           where revision.EditorId == userId
                           select new { revision.Title, revision.Version, revision.EditedAt }).ToListAsync(cancellationToken);
        return new PersonalDataSection("community", new { ForumTopics = topics, ForumPosts = posts, MessagesSent = messages, WikiEdits = edits });
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.Posts.Where(p => p.AuthorId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Body, ErasedText).SetProperty(p => p.RemovedAt, p => p.RemovedAt ?? now), cancellationToken);
        await db.Topics.Where(t => t.AuthorId == userId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Title, ErasedText).SetProperty(t => t.Body, ErasedText), cancellationToken);
        await db.Messages.Where(m => m.SenderId == userId).ExecuteUpdateAsync(s => s.SetProperty(m => m.Body, ErasedText), cancellationToken);
    }
}
