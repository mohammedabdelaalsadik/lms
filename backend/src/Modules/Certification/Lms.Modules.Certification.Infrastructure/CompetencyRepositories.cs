using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lms.Modules.Certification.Infrastructure;

internal sealed class CompetencyRepository(CertificationDbContext db) : ICompetencyRepository
{
    public Task<Competency?> FindAsync(CompetencyId id, CancellationToken cancellationToken) =>
        db.Competencies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken) =>
        await db.Competencies.AsNoTracking().OrderBy(c => c.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Competency>> GetAsync(IReadOnlyCollection<CompetencyId> ids, CancellationToken cancellationToken) =>
        await db.Competencies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);

    public Task<bool> CodeTakenAsync(string code, CancellationToken cancellationToken) =>
        db.Competencies.AnyAsync(c => c.Code == code, cancellationToken);

    public Task<CompetencySignOff?> FindSignOffAsync(CompetencyId id, Guid traineeId, CancellationToken cancellationToken) =>
        db.CompetencySignOffs.FirstOrDefaultAsync(s => s.CompetencyId == id && s.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlyList<CompetencySignOff>> ListSignOffsAsync(IReadOnlyCollection<Guid> traineeIds, CancellationToken cancellationToken) =>
        await db.CompetencySignOffs.AsNoTracking().Where(s => traineeIds.Contains(s.TraineeId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, HashSet<Guid>>> GetCertifiedCoursesAsync(IReadOnlyCollection<Guid> traineeIds, CancellationToken cancellationToken)
    {
        var rows = await db.Certificates.AsNoTracking()
            .Where(c => traineeIds.Contains(c.TraineeId) && c.Status == CertificateStatus.Issued)
            .Select(c => new { c.TraineeId, c.CourseId })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.TraineeId).ToDictionary(g => g.Key, g => g.Select(r => r.CourseId).ToHashSet());
    }

    public void Add(Competency competency) => db.Competencies.Add(competency);

    public void Add(CompetencySignOff signOff) => db.CompetencySignOffs.Add(signOff);

    public void Remove(CompetencySignOff signOff) => db.CompetencySignOffs.Remove(signOff);
}

internal sealed class LearningPlanRepository(CertificationDbContext db) : ILearningPlanRepository
{
    public Task<LearningPlan?> FindAsync(LearningPlanId id, CancellationToken cancellationToken) =>
        db.LearningPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LearningPlan>> ListAsync(CancellationToken cancellationToken) =>
        await db.LearningPlans.AsNoTracking().OrderBy(p => p.Name.En).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<LearningPlanId, int>> CountAssignmentsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.PlanAssignments.AsNoTracking().GroupBy(a => a.PlanId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.Count);
    }

    public async Task<IReadOnlyList<PlanAssignment>> ListAssignmentsAsync(LearningPlanId id, CancellationToken cancellationToken) =>
        await db.PlanAssignments.AsNoTracking().Where(a => a.PlanId == id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlanAssignment>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken) =>
        await db.PlanAssignments.AsNoTracking().Where(a => a.TraineeId == traineeId).OrderByDescending(a => a.AssignedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LearningPlan>> GetAsync(IReadOnlyCollection<LearningPlanId> ids, CancellationToken cancellationToken) =>
        await db.LearningPlans.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<PlanAssignment?> FindAssignmentAsync(LearningPlanId id, Guid traineeId, CancellationToken cancellationToken) =>
        db.PlanAssignments.FirstOrDefaultAsync(a => a.PlanId == id && a.TraineeId == traineeId, cancellationToken);

    public void Add(LearningPlan plan) => db.LearningPlans.Add(plan);

    public void Add(PlanAssignment assignment) => db.PlanAssignments.Add(assignment);

    public void Remove(PlanAssignment assignment) => db.PlanAssignments.Remove(assignment);
}

internal sealed class LearningPlanAssignedProcessor(IServiceScopeFactory scopes, ILogger<LearningPlanAssignedProcessor> logger)
    : OutboxProcessor<CertificationDbContext>(scopes, logger)
{
    protected override string EventType => typeof(LearningPlanAssigned).FullName!;

    protected override Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var assigned = JsonSerializer.Deserialize<LearningPlanAssigned>(payload)!;

        return services.GetRequiredService<INotifier>().NotifyAsync(
            new NotificationRequest(assigned.TraineeId, NotificationKind.LearningPlanAssigned, $"plan:{assigned.AssignmentId}", null, assigned.PlanNameAr, assigned.PlanNameEn, "/my-plans"),
            cancellationToken);
    }
}
