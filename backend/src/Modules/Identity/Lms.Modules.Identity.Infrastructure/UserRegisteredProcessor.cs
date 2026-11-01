using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Identity.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Identity.Infrastructure;

internal sealed class UserRegisteredProcessor(IServiceScopeFactory scopes, ILogger<UserRegisteredProcessor> logger)
    : OutboxProcessor<IdentityDbContext>(scopes, logger)
{
    protected override string EventType => typeof(UserRegistered).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var registered = JsonSerializer.Deserialize<UserRegistered>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(registered.UserId, NotificationKind.Welcome, $"welcome:{registered.UserId}", null, null, null, "/dashboard"),
            cancellationToken);
    }
}
