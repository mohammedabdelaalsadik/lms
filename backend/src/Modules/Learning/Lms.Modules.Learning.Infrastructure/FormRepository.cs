using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Learning.Infrastructure;

internal sealed class FormRepository(LearningDbContext db) : IFormRepository
{
    public Task<bool> HasSubmittedAsync(ActivityId id, Guid cohortId, Guid traineeId, CancellationToken cancellationToken) =>
        db.FormSubmissions.AnyAsync(s => s.ActivityId == id && s.CohortId == cohortId && s.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlySet<ActivityId>> ListSubmittedAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken)
    {
        var ids = await db.FormSubmissions.AsNoTracking()
            .Where(s => s.CohortId == cohortId && s.TraineeId == traineeId)
            .Select(s => s.ActivityId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<FormResponse>> ListResponsesAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.FormResponses.AsNoTracking().Where(r => r.CohortId == cohortId).OrderBy(r => r.Id).ToListAsync(cancellationToken);

    public void Add(FormSubmission submission) => db.FormSubmissions.Add(submission);

    public void Add(FormResponse response) => db.FormResponses.Add(response);
}
