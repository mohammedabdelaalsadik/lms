using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Enrollment.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Enrollment.Infrastructure;

internal sealed class TraineeEnrolledProcessor(IServiceScopeFactory scopes, ILogger<TraineeEnrolledProcessor> logger)
    : OutboxProcessor<EnrollmentDbContext>(scopes, logger)
{
    protected override string EventType => typeof(TraineeEnrolled).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var enrolled = JsonSerializer.Deserialize<TraineeEnrolled>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(enrolled.TraineeId, NotificationKind.EnrollmentConfirmed, $"enrolled:{enrolled.EnrollmentId}", enrolled.CourseId, null, null, "/my-enrollments"),
            cancellationToken);
    }
}

internal sealed class EnrollmentRejectedProcessor(IServiceScopeFactory scopes, ILogger<EnrollmentRejectedProcessor> logger)
    : OutboxProcessor<EnrollmentDbContext>(scopes, logger)
{
    protected override string EventType => typeof(EnrollmentRejected).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var rejected = JsonSerializer.Deserialize<EnrollmentRejected>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(rejected.TraineeId, NotificationKind.EnrollmentRejected, $"rejected:{rejected.EnrollmentId}", rejected.CourseId, rejected.Reason, rejected.Reason, "/my-enrollments"),
            cancellationToken);
    }
}
