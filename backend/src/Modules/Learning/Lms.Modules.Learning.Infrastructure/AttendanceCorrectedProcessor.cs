using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Learning.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Learning.Infrastructure;

/// <summary>Copies each attendance correction, with old and new status, into the audit log.</summary>
internal sealed class AttendanceCorrectedProcessor(IServiceScopeFactory scopes, ILogger<AttendanceCorrectedProcessor> logger)
    : OutboxProcessor<LearningDbContext>(scopes, logger)
{
    protected override string EventType => typeof(AttendanceCorrected).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var corrected = JsonSerializer.Deserialize<AttendanceCorrected>(payload)!;
        var clock = services.GetRequiredService<TimeProvider>();
        var details = JsonSerializer.Serialize(new
        {
            corrected.SessionId,
            corrected.TraineeId,
            OldStatus = corrected.OldStatus?.ToString(),
            NewStatus = corrected.NewStatus.ToString(),
        });

        var record = new AuditRecord(clock.GetUtcNow(), corrected.ChangedBy, null, null, nameof(AttendanceCorrected), details, true, null);
        return services.GetRequiredService<IAuditTrail>().RecordAsync(record, cancellationToken);
    }
}
