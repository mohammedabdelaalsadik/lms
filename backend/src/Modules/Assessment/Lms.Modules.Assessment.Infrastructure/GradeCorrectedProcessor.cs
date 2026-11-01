using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Assessment.Infrastructure;

/// <summary>
/// Copies each approved grade correction, with old and new grade, into the audit log and tells the
/// trainee. One processor does both because a stored event is marked done once. The notification
/// goes first because it is safe to repeat, so a retry can't write the audit entry twice.
/// </summary>
internal sealed class GradeCorrectedProcessor(IServiceScopeFactory scopes, ILogger<GradeCorrectedProcessor> logger)
    : OutboxProcessor<AssessmentDbContext>(scopes, logger)
{
    protected override string EventType => typeof(GradeCorrected).FullName!;

    protected override async Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var corrected = JsonSerializer.Deserialize<GradeCorrected>(payload)!;
        var cohort = await services.GetRequiredService<ICohortDirectory>().FindAsync(corrected.CohortId, cancellationToken);

        await services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(
                corrected.TraineeId,
                NotificationKind.GradeCorrected,
                $"gradefix:{corrected.GradeItemId}:{corrected.TraineeId}:{corrected.OldPercent}:{corrected.NewPercent}",
                cohort?.CourseId,
                null,
                null,
                $"/learn/{corrected.CohortId}/assessments"),
            cancellationToken);

        var clock = services.GetRequiredService<TimeProvider>();
        var record = new AuditRecord(clock.GetUtcNow(), corrected.ApprovedBy, null, null, nameof(GradeCorrected), payload, true, null);
        await services.GetRequiredService<IAuditTrail>().RecordAsync(record, cancellationToken);
    }
}
