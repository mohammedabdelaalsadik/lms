using Lms.Modules.Assessment.Application;
using Lms.Modules.Assessment.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Assessment.Infrastructure;

internal sealed class WorkshopRepository(AssessmentDbContext db) : IWorkshopRepository
{
    public Task<Workshop?> FindAsync(WorkshopId id, CancellationToken cancellationToken) =>
        db.Workshops.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Workshop>> ListAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.Workshops.AsNoTracking().Where(w => w.CohortId == cohortId).OrderBy(w => w.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkshopSubmission>> ListSubmissionsAsync(IReadOnlyCollection<WorkshopId> ids, CancellationToken cancellationToken) =>
        await db.WorkshopSubmissions.AsNoTracking().Where(s => ids.Contains(s.WorkshopId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkshopReview>> ListReviewsAsync(IReadOnlyCollection<WorkshopId> ids, CancellationToken cancellationToken) =>
        await db.WorkshopReviews.AsNoTracking().Where(r => ids.Contains(r.WorkshopId)).ToListAsync(cancellationToken);

    public Task<WorkshopSubmission?> FindSubmissionAsync(WorkshopId id, Guid traineeId, CancellationToken cancellationToken) =>
        db.WorkshopSubmissions.FirstOrDefaultAsync(s => s.WorkshopId == id && s.TraineeId == traineeId, cancellationToken);

    public Task<WorkshopReview?> FindReviewAsync(Guid id, CancellationToken cancellationToken) =>
        db.WorkshopReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(Workshop workshop) => db.Workshops.Add(workshop);

    public void Add(WorkshopSubmission submission) => db.WorkshopSubmissions.Add(submission);

    public void Add(WorkshopReview review) => db.WorkshopReviews.Add(review);
}
