using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Certification.Contracts;
using Lms.Modules.Learning.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Learning.Infrastructure;

/// <summary>Turns each recorded course completion into a certificate.</summary>
internal sealed class CourseCompletedProcessor(IServiceScopeFactory scopes, ILogger<CourseCompletedProcessor> logger)
    : OutboxProcessor<LearningDbContext>(scopes, logger)
{
    protected override string EventType => typeof(CourseCompleted).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var completed = JsonSerializer.Deserialize<CourseCompleted>(payload)!;
        var issuer = services.GetRequiredService<ICertificateIssuer>();
        return issuer.IssueForCompletionAsync(completed.CohortId, completed.CourseId, completed.TraineeId, cancellationToken);
    }
}
