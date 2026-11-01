using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Certification.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Certification.Application;

public interface ICompetencyRepository
{
    Task<Competency?> FindAsync(CompetencyId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Competency>> GetAsync(IReadOnlyCollection<CompetencyId> ids, CancellationToken cancellationToken);

    Task<bool> CodeTakenAsync(string code, CancellationToken cancellationToken);

    Task<CompetencySignOff?> FindSignOffAsync(CompetencyId id, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompetencySignOff>> ListSignOffsAsync(IReadOnlyCollection<Guid> traineeIds, CancellationToken cancellationToken);

    /// <summary>Courses with a certificate that is still valid, per trainee.</summary>
    Task<IReadOnlyDictionary<Guid, HashSet<Guid>>> GetCertifiedCoursesAsync(IReadOnlyCollection<Guid> traineeIds, CancellationToken cancellationToken);

    void Add(Competency competency);

    void Add(CompetencySignOff signOff);

    void Remove(CompetencySignOff signOff);
}

public interface ILearningPlanRepository
{
    Task<LearningPlan?> FindAsync(LearningPlanId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningPlan>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<LearningPlanId, int>> CountAssignmentsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanAssignment>> ListAssignmentsAsync(LearningPlanId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanAssignment>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningPlan>> GetAsync(IReadOnlyCollection<LearningPlanId> ids, CancellationToken cancellationToken);

    Task<PlanAssignment?> FindAssignmentAsync(LearningPlanId id, Guid traineeId, CancellationToken cancellationToken);

    void Add(LearningPlan plan);

    void Add(PlanAssignment assignment);

    void Remove(PlanAssignment assignment);
}

public sealed record CourseRef(Guid Id, string Code, TextDto Title);

public sealed record CompetencyResponse(Guid Id, string Code, TextDto Name, TextDto Description, IReadOnlyList<CourseRef> Courses, bool IsRetired);

internal static class CompetencyMapping
{
    public static TextDto ToDto(this LocalizedText text) => new(text.Ar, text.En);

    public static async Task<IReadOnlyDictionary<Guid, CourseRef>> ReadCoursesAsync(this ICourseCatalog catalog, IEnumerable<Competency> competencies, CancellationToken cancellationToken)
    {
        var ids = competencies.SelectMany(c => c.Courses).Select(c => c.CourseId).Distinct().ToList();
        var courses = await catalog.GetAsync(ids, cancellationToken);
        return courses.ToDictionary(c => c.Key, c => new CourseRef(c.Key, c.Value.Code, new TextDto(c.Value.TitleAr, c.Value.TitleEn)));
    }

    public static IReadOnlyList<CourseRef> LinkedCourses(this Competency competency, IReadOnlyDictionary<Guid, CourseRef> courses) =>
        [.. competency.Courses.Select(c => courses.GetValueOrDefault(c.CourseId)).OfType<CourseRef>().OrderBy(c => c.Code)];

    public static CompetencyResponse ToResponse(this Competency c, IReadOnlyDictionary<Guid, CourseRef> courses) =>
        new(c.Id.Value, c.Code, c.Name.ToDto(), c.Description.ToDto(), c.LinkedCourses(courses), c.RetiredAt is not null);
}

public sealed record ListCompetenciesQuery : IQuery<IReadOnlyList<CompetencyResponse>>;

internal sealed class ListCompetenciesHandler(ICompetencyRepository competencies, ICourseCatalog catalog) : IRequestHandler<ListCompetenciesQuery, IReadOnlyList<CompetencyResponse>>
{
    public async Task<Result<IReadOnlyList<CompetencyResponse>>> Handle(ListCompetenciesQuery request, CancellationToken cancellationToken)
    {
        var list = await competencies.ListAsync(cancellationToken);
        var courses = await catalog.ReadCoursesAsync(list, cancellationToken);
        IReadOnlyList<CompetencyResponse> items = [.. list.Select(c => c.ToResponse(courses))];
        return Result<IReadOnlyList<CompetencyResponse>>.Success(items);
    }
}

public sealed record CreateCompetencyCommand(string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CourseIds) : ICommand<Guid>;

internal sealed class CreateCompetencyValidator : AbstractValidator<CreateCompetencyCommand>
{
    public CreateCompetencyValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Competency.CodeMaxLength);
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(Competency.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(Competency.NameMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(Competency.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(Competency.DescriptionMaxLength);
        RuleFor(c => c.CourseIds).NotNull();
    }
}

internal sealed class CreateCompetencyHandler(ICompetencyRepository competencies, ICertificationUnitOfWork unitOfWork) : IRequestHandler<CreateCompetencyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCompetencyCommand request, CancellationToken cancellationToken)
    {
        var name = LocalizedText.Create(request.NameAr, request.NameEn, Competency.NameMaxLength);
        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, Competency.DescriptionMaxLength);
        if (!name.IsSuccess || !description.IsSuccess)
        {
            return (name.Error ?? description.Error)!;
        }

        var competency = Competency.Create(request.Code, name.Value, description.Value, request.CourseIds);
        if (!competency.IsSuccess)
        {
            return competency.Error!;
        }

        if (await competencies.CodeTakenAsync(competency.Value.Code, cancellationToken))
        {
            return CompetencyErrors.CodeTaken;
        }

        competencies.Add(competency.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return competency.Value.Id.Value;
    }
}

public sealed record UpdateCompetencyCommand(Guid Id, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CourseIds) : ICommand<Unit>;

internal sealed class UpdateCompetencyValidator : AbstractValidator<UpdateCompetencyCommand>
{
    public UpdateCompetencyValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(Competency.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(Competency.NameMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(Competency.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(Competency.DescriptionMaxLength);
        RuleFor(c => c.CourseIds).NotNull();
    }
}

internal sealed class UpdateCompetencyHandler(ICompetencyRepository competencies, ICertificationUnitOfWork unitOfWork) : IRequestHandler<UpdateCompetencyCommand, Unit>
{
    public async Task<Result<Unit>> Handle(UpdateCompetencyCommand request, CancellationToken cancellationToken)
    {
        var competency = await competencies.FindAsync(new CompetencyId(request.Id), cancellationToken);
        if (competency is null)
        {
            return CompetencyErrors.NotFound;
        }

        var name = LocalizedText.Create(request.NameAr, request.NameEn, Competency.NameMaxLength);
        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, Competency.DescriptionMaxLength);
        if (!name.IsSuccess || !description.IsSuccess)
        {
            return (name.Error ?? description.Error)!;
        }

        competency.Update(name.Value, description.Value, request.CourseIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RetireCompetencyCommand(Guid Id) : ICommand<Unit>;

internal sealed class RetireCompetencyHandler(ICompetencyRepository competencies, ICertificationUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RetireCompetencyCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RetireCompetencyCommand request, CancellationToken cancellationToken)
    {
        var competency = await competencies.FindAsync(new CompetencyId(request.Id), cancellationToken);
        if (competency is null)
        {
            return CompetencyErrors.NotFound;
        }

        competency.Retire(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record SetSignOffCommand(Guid CompetencyId, Guid TraineeId, bool Met) : ICommand<Unit>;

internal sealed class SetSignOffHandler(
    ICompetencyRepository competencies,
    IUserDirectory directory,
    ICurrentUser user,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SetSignOffCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SetSignOffCommand request, CancellationToken cancellationToken)
    {
        var id = new CompetencyId(request.CompetencyId);
        if (await competencies.FindAsync(id, cancellationToken) is null)
        {
            return CompetencyErrors.NotFound;
        }

        var people = await directory.GetAsync([request.TraineeId], cancellationToken);
        if (!people.TryGetValue(request.TraineeId, out var person) || person.Role != LmsRoles.Trainee)
        {
            return CompetencyErrors.TraineeNotFound;
        }

        var existing = await competencies.FindSignOffAsync(id, request.TraineeId, cancellationToken);
        if (request.Met && existing is null)
        {
            competencies.Add(CompetencySignOff.Create(id, request.TraineeId, user.Id!.Value, clock.GetUtcNow()));
        }
        else if (!request.Met && existing is not null)
        {
            competencies.Remove(existing);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record PlanResponse(Guid Id, TextDto Name, TextDto Description, IReadOnlyList<Guid> CompetencyIds, int Assigned, bool IsRetired);

public sealed record ListPlansQuery : IQuery<IReadOnlyList<PlanResponse>>;

internal sealed class ListPlansHandler(ILearningPlanRepository plans) : IRequestHandler<ListPlansQuery, IReadOnlyList<PlanResponse>>
{
    public async Task<Result<IReadOnlyList<PlanResponse>>> Handle(ListPlansQuery request, CancellationToken cancellationToken)
    {
        var list = await plans.ListAsync(cancellationToken);
        var counts = await plans.CountAssignmentsAsync(cancellationToken);
        IReadOnlyList<PlanResponse> items =
        [
            .. list.Select(p => new PlanResponse(
                p.Id.Value,
                p.Name.ToDto(),
                p.Description.ToDto(),
                [.. p.OrderedCompetencies().Select(c => c.Value)],
                counts.GetValueOrDefault(p.Id),
                p.RetiredAt is not null)),
        ];

        return Result<IReadOnlyList<PlanResponse>>.Success(items);
    }
}

public sealed record SavePlanCommand(Guid? Id, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CompetencyIds) : ICommand<Guid>;

internal sealed class SavePlanValidator : AbstractValidator<SavePlanCommand>
{
    public SavePlanValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(LearningPlan.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(LearningPlan.NameMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(LearningPlan.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(LearningPlan.DescriptionMaxLength);
        RuleFor(c => c.CompetencyIds).NotNull();
    }
}

internal sealed class SavePlanHandler(ILearningPlanRepository plans, ICompetencyRepository competencies, ICertificationUnitOfWork unitOfWork) : IRequestHandler<SavePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SavePlanCommand request, CancellationToken cancellationToken)
    {
        LearningPlan? plan = null;
        if (request.Id is { } id)
        {
            plan = await plans.FindAsync(new LearningPlanId(id), cancellationToken);
            if (plan is null)
            {
                return CompetencyErrors.PlanNotFound;
            }
        }

        var name = LocalizedText.Create(request.NameAr, request.NameEn, LearningPlan.NameMaxLength);
        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, LearningPlan.DescriptionMaxLength);
        if (!name.IsSuccess || !description.IsSuccess)
        {
            return (name.Error ?? description.Error)!;
        }

        List<CompetencyId> wanted = [.. request.CompetencyIds.Distinct().Select(c => new CompetencyId(c))];
        var found = await competencies.GetAsync(wanted, cancellationToken);
        if (found.Count != wanted.Count)
        {
            return CompetencyErrors.NotFound;
        }

        // A plan may keep a competency retired after it was added, but cannot take on a new one.
        var kept = plan?.OrderedCompetencies() ?? [];
        if (found.Any(c => c.RetiredAt is not null && !kept.Contains(c.Id)))
        {
            return CompetencyErrors.CompetencyRetired;
        }

        if (plan is not null)
        {
            var updated = plan.Update(name.Value, description.Value, wanted);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }
        else
        {
            var created = LearningPlan.Create(name.Value, description.Value, wanted);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            plan = created.Value;
            plans.Add(plan);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return plan.Id.Value;
    }
}

public sealed record RetirePlanCommand(Guid Id) : ICommand<Unit>;

internal sealed class RetirePlanHandler(ILearningPlanRepository plans, ICertificationUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RetirePlanCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RetirePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(new LearningPlanId(request.Id), cancellationToken);
        if (plan is null)
        {
            return CompetencyErrors.PlanNotFound;
        }

        plan.Retire(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

/// <summary>Returns how many trainees were added. Trainees who already have the plan are left as they are.</summary>
public sealed record AssignPlanToCohortCommand(Guid PlanId, Guid CohortId, DateOnly? DueOn) : ICommand<int>;

internal sealed class AssignPlanToCohortHandler(
    ILearningPlanRepository plans,
    ICohortDirectory cohorts,
    ICurrentUser user,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<AssignPlanToCohortCommand, int>
{
    public async Task<Result<int>> Handle(AssignPlanToCohortCommand request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(new LearningPlanId(request.PlanId), cancellationToken);
        if (plan is null)
        {
            return CompetencyErrors.PlanNotFound;
        }

        if (plan.RetiredAt is not null)
        {
            return CompetencyErrors.PlanRetired;
        }

        if (await cohorts.FindAsync(request.CohortId, cancellationToken) is null)
        {
            return CompetencyErrors.CohortNotFound;
        }

        var existing = (await plans.ListAssignmentsAsync(plan.Id, cancellationToken)).Select(a => a.TraineeId).ToHashSet();
        var trainees = await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken);
        var added = 0;

        foreach (var trainee in trainees.Where(t => !existing.Contains(t)))
        {
            var assignment = PlanAssignment.Create(plan, trainee, request.DueOn, user.Id!.Value, clock.GetUtcNow());
            if (!assignment.IsSuccess)
            {
                return assignment.Error!;
            }

            plans.Add(assignment.Value);
            added++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>Returns 1 when added, or 0 when the trainee already has the plan.</summary>
public sealed record AssignPlanToTraineeCommand(Guid PlanId, Guid TraineeId, DateOnly? DueOn) : ICommand<int>;

internal sealed class AssignPlanToTraineeHandler(
    ILearningPlanRepository plans,
    IUserDirectory directory,
    ICurrentUser user,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<AssignPlanToTraineeCommand, int>
{
    public async Task<Result<int>> Handle(AssignPlanToTraineeCommand request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(new LearningPlanId(request.PlanId), cancellationToken);
        if (plan is null)
        {
            return CompetencyErrors.PlanNotFound;
        }

        var people = await directory.GetAsync([request.TraineeId], cancellationToken);
        if (!people.TryGetValue(request.TraineeId, out var person) || person.Role != LmsRoles.Trainee)
        {
            return CompetencyErrors.TraineeNotFound;
        }

        var assignment = PlanAssignment.Create(plan, request.TraineeId, request.DueOn, user.Id!.Value, clock.GetUtcNow());
        if (!assignment.IsSuccess)
        {
            return assignment.Error!;
        }

        if (await plans.FindAssignmentAsync(plan.Id, request.TraineeId, cancellationToken) is not null)
        {
            return 0;
        }

        plans.Add(assignment.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return 1;
    }
}

public sealed record UnassignPlanCommand(Guid PlanId, Guid TraineeId) : ICommand<Unit>;

internal sealed class UnassignPlanHandler(ILearningPlanRepository plans, ICertificationUnitOfWork unitOfWork) : IRequestHandler<UnassignPlanCommand, Unit>
{
    public async Task<Result<Unit>> Handle(UnassignPlanCommand request, CancellationToken cancellationToken)
    {
        var assignment = await plans.FindAssignmentAsync(new LearningPlanId(request.PlanId), request.TraineeId, cancellationToken);
        if (assignment is null)
        {
            return CompetencyErrors.TraineeNotFound;
        }

        plans.Remove(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record PlanColumn(Guid Id, string Code, TextDto Name);

public sealed record PlanTraineeRow(Guid TraineeId, TextDto Name, DateOnly? DueOn, IReadOnlyDictionary<Guid, AttainmentSource> Cells, int Met);

public sealed record PlanProgressResponse(Guid Id, TextDto Name, TextDto Description, bool IsRetired, IReadOnlyList<PlanColumn> Competencies, IReadOnlyList<PlanTraineeRow> Trainees);

public sealed record GetPlanProgressQuery(Guid Id) : IQuery<PlanProgressResponse>;

internal sealed class GetPlanProgressHandler(ILearningPlanRepository plans, ICompetencyRepository competencies, IUserDirectory directory) : IRequestHandler<GetPlanProgressQuery, PlanProgressResponse>
{
    public async Task<Result<PlanProgressResponse>> Handle(GetPlanProgressQuery request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(new LearningPlanId(request.Id), cancellationToken);
        if (plan is null)
        {
            return CompetencyErrors.PlanNotFound;
        }

        var order = plan.OrderedCompetencies();
        var list = await competencies.GetAsync(order, cancellationToken);
        var ordered = order.Select(id => list.FirstOrDefault(c => c.Id == id)).OfType<Competency>().ToList();
        var assignments = await plans.ListAssignmentsAsync(plan.Id, cancellationToken);
        List<Guid> ids = [.. assignments.Select(a => a.TraineeId)];
        var people = await directory.GetAsync(ids, cancellationToken);
        var certified = await competencies.GetCertifiedCoursesAsync(ids, cancellationToken);
        var signOffs = (await competencies.ListSignOffsAsync(ids, cancellationToken)).ToLookup(s => s.TraineeId, s => s.CompetencyId);

        var rows = new List<PlanTraineeRow>();
        foreach (var assignment in assignments)
        {
            if (!people.TryGetValue(assignment.TraineeId, out var person))
            {
                continue;
            }

            var mine = certified.GetValueOrDefault(assignment.TraineeId) ?? [];
            var signed = signOffs[assignment.TraineeId].ToHashSet();
            var cells = ordered.ToDictionary(c => c.Id.Value, c => Attainment.For(c, mine, signed));
            rows.Add(new PlanTraineeRow(assignment.TraineeId, new TextDto(person.NameAr, person.NameEn), assignment.DueOn, cells, cells.Values.Count(v => v != AttainmentSource.None)));
        }

        return new PlanProgressResponse(
            plan.Id.Value,
            plan.Name.ToDto(),
            plan.Description.ToDto(),
            plan.RetiredAt is not null,
            [.. ordered.Select(c => new PlanColumn(c.Id.Value, c.Code, c.Name.ToDto()))],
            [.. rows.OrderBy(r => r.Name.Ar)]);
    }
}

public sealed record MyCompetency(Guid Id, string Code, TextDto Name, TextDto Description, AttainmentSource Source, IReadOnlyList<CourseRef> Courses);

public sealed record MyPlan(Guid Id, TextDto Name, TextDto Description, DateOnly? DueOn, int Met, IReadOnlyList<MyCompetency> Competencies);

public sealed record ListMyPlansQuery : IQuery<IReadOnlyList<MyPlan>>;

internal sealed class ListMyPlansHandler(ILearningPlanRepository plans, ICompetencyRepository competencies, ICourseCatalog catalog, ICurrentUser user) : IRequestHandler<ListMyPlansQuery, IReadOnlyList<MyPlan>>
{
    public async Task<Result<IReadOnlyList<MyPlan>>> Handle(ListMyPlansQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        var assignments = await plans.ListForTraineeAsync(me, cancellationToken);
        var list = await plans.GetAsync([.. assignments.Select(a => a.PlanId)], cancellationToken);
        var wanted = list.SelectMany(p => p.OrderedCompetencies()).Distinct().ToList();
        var skills = await competencies.GetAsync(wanted, cancellationToken);
        var courses = await catalog.ReadCoursesAsync(skills, cancellationToken);
        var certified = (await competencies.GetCertifiedCoursesAsync([me], cancellationToken)).GetValueOrDefault(me) ?? [];
        var signed = (await competencies.ListSignOffsAsync([me], cancellationToken)).Select(s => s.CompetencyId).ToHashSet();

        var result = new List<MyPlan>();
        foreach (var assignment in assignments)
        {
            var plan = list.FirstOrDefault(p => p.Id == assignment.PlanId);
            if (plan is null)
            {
                continue;
            }

            List<MyCompetency> rows =
            [
                .. plan.OrderedCompetencies()
                    .Select(id => skills.FirstOrDefault(c => c.Id == id))
                    .OfType<Competency>()
                    .Select(c => new MyCompetency(c.Id.Value, c.Code, c.Name.ToDto(), c.Description.ToDto(), Attainment.For(c, certified, signed), c.LinkedCourses(courses))),
            ];

            result.Add(new MyPlan(plan.Id.Value, plan.Name.ToDto(), plan.Description.ToDto(), assignment.DueOn, rows.Count(r => r.Source != AttainmentSource.None), rows));
        }

        return Result<IReadOnlyList<MyPlan>>.Success(result);
    }
}
