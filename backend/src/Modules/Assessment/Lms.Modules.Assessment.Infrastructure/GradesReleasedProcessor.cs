using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Assessment.Infrastructure;

/// <summary>Tells every enrolled trainee that an item's grades are out. A retry skips those already told.</summary>
internal sealed class GradesReleasedProcessor(IServiceScopeFactory scopes, ILogger<GradesReleasedProcessor> logger)
    : OutboxProcessor<AssessmentDbContext>(scopes, logger)
{
    protected override string EventType => typeof(GradesReleased).FullName!;

    protected override async Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var released = JsonSerializer.Deserialize<GradesReleased>(payload)!;
        var cohorts = services.GetRequiredService<ICohortDirectory>();
        var notifier = services.GetRequiredService<INotifier>();
        var cohort = await cohorts.FindAsync(released.CohortId, cancellationToken);

        if (cohort is null)
        {
            return;
        }

        foreach (var traineeId in await cohorts.GetEnrolledTraineesAsync(released.CohortId, cancellationToken))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    traineeId,
                    NotificationKind.GradesPublished,
                    $"grades:{released.GradeItemId}:{traineeId}",
                    cohort.CourseId,
                    released.TitleAr,
                    released.TitleEn,
                    $"/learn/{released.CohortId}/assessments"),
                cancellationToken);
        }
    }
}
