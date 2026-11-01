using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Certification.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Certification.Infrastructure;

internal sealed class CertificateIssuedProcessor(IServiceScopeFactory scopes, ILogger<CertificateIssuedProcessor> logger)
    : OutboxProcessor<CertificationDbContext>(scopes, logger)
{
    protected override string EventType => typeof(CertificateIssued).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var issued = JsonSerializer.Deserialize<CertificateIssued>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(issued.TraineeId, NotificationKind.CertificateIssued, $"certificate:{issued.CertificateId}", issued.CourseId, null, null, "/certificates"),
            cancellationToken);
    }
}

internal sealed class CertificateRevokedProcessor(IServiceScopeFactory scopes, ILogger<CertificateRevokedProcessor> logger)
    : OutboxProcessor<CertificationDbContext>(scopes, logger)
{
    protected override string EventType => typeof(CertificateRevoked).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var revoked = JsonSerializer.Deserialize<CertificateRevoked>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(revoked.TraineeId, NotificationKind.CertificateRevoked, $"revoked:{revoked.CertificateId}", revoked.CourseId, revoked.Reason, revoked.Reason, "/certificates"),
            cancellationToken);
    }
}
