using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Identity.Infrastructure;

internal sealed class PrivacyRequestRepository(IdentityDbContext db) : IPrivacyRequestRepository
{
    public Task<PrivacyRequest?> FindAsync(PrivacyRequestId id, CancellationToken cancellationToken) =>
        db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<bool> HasPendingAsync(Guid userId, PrivacyRequestKind kind, CancellationToken cancellationToken) =>
        db.PrivacyRequests.AnyAsync(r => r.UserId == userId && r.Kind == kind && r.Status == PrivacyRequestStatus.Pending, cancellationToken);

    public async Task<IReadOnlyList<PrivacyRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.PrivacyRequests.AsNoTracking().Where(r => r.UserId == userId).OrderByDescending(r => r.RequestedAt).ToListAsync(cancellationToken);

    /// <summary>Waiting requests first, oldest at the top, so nothing sinks out of sight.</summary>
    public async Task<IReadOnlyList<PrivacyRequest>> ListRecentAsync(int take, CancellationToken cancellationToken)
    {
        var pending = await db.PrivacyRequests.AsNoTracking().Where(r => r.Status == PrivacyRequestStatus.Pending).OrderBy(r => r.RequestedAt).Take(take).ToListAsync(cancellationToken);
        var rest = await db.PrivacyRequests.AsNoTracking().Where(r => r.Status != PrivacyRequestStatus.Pending).OrderByDescending(r => r.RequestedAt).Take(take - pending.Count).ToListAsync(cancellationToken);
        return [.. pending, .. rest];
    }

    public void Add(PrivacyRequest request) => db.PrivacyRequests.Add(request);
}

internal sealed class PrivacyRequestedProcessor(IServiceScopeFactory scopes, ILogger<PrivacyRequestedProcessor> logger)
    : OutboxProcessor<IdentityDbContext>(scopes, logger)
{
    protected override string EventType => typeof(PrivacyRequested).FullName!;

    protected override async Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var requested = JsonSerializer.Deserialize<PrivacyRequested>(payload)!;
        var notifier = services.GetRequiredService<INotifier>();

        foreach (var admin in await services.GetRequiredService<IUserRepository>().ListByRoleAsync(Role.Admin, cancellationToken))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(admin.Id, NotificationKind.PrivacyRequestReceived, $"privacy:{requested.RequestId}:{admin.Id}", null, null, null, "/privacy/requests"),
                cancellationToken);
        }
    }
}

internal sealed class PrivacyRequestDecidedProcessor(IServiceScopeFactory scopes, ILogger<PrivacyRequestDecidedProcessor> logger)
    : OutboxProcessor<IdentityDbContext>(scopes, logger)
{
    protected override string EventType => typeof(PrivacyRequestDecided).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var decided = JsonSerializer.Deserialize<PrivacyRequestDecided>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(decided.UserId, NotificationKind.PrivacyRequestDecided, $"privacy-decided:{decided.RequestId}", null, null, null, "/privacy"),
            cancellationToken);
    }
}
