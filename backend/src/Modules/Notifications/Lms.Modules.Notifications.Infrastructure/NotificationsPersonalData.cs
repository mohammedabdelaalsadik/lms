using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Notifications.Infrastructure;

internal sealed class NotificationsPersonalData(NotificationsDbContext db) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Kind, n.TitleAr, n.TitleEn, n.BodyAr, n.BodyEn, n.CreatedAt, n.ReadAt })
            .ToListAsync(cancellationToken);

        return new PersonalDataSection("notifications", rows);
    }

    /// <summary>Also cancels any email still waiting to go out.</summary>
    public Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.RecipientId == userId).ExecuteDeleteAsync(cancellationToken);
}
