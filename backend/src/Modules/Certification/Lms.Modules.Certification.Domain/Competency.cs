using Lms.SharedKernel;
using System.Text.RegularExpressions;

namespace Lms.Modules.Certification.Domain;

public readonly record struct CompetencyId(Guid Value);

public readonly record struct LearningPlanId(Guid Value);

public readonly record struct PlanAssignmentId(Guid Value);

public sealed record LearningPlanAssigned(Guid AssignmentId, Guid PlanId, Guid TraineeId, string PlanNameAr, string PlanNameEn) : IDomainEvent;

public static class CompetencyErrors
{
    public static readonly Error NotFound = Error.NotFound("certification.competency_not_found", "Competency was not found.");
    public static readonly Error InvalidCode = Error.Validation("certification.competency_invalid_code", "Use letters, numbers and dashes only.");
    public static readonly Error CodeTaken = Error.Conflict("certification.competency_code_taken", "This competency code is already in use.");
    public static readonly Error PlanNotFound = Error.NotFound("certification.plan_not_found", "Learning plan was not found.");
    public static readonly Error PlanEmpty = Error.Validation("certification.plan_empty", "Choose at least one competency.");
    public static readonly Error PlanRetired = Error.Conflict("certification.plan_retired", "This learning plan is retired.");
    public static readonly Error CompetencyRetired = Error.Conflict("certification.competency_retired", "A retired competency cannot be added.");
    public static readonly Error DueInPast = Error.Validation("certification.plan_due_in_past", "The due date cannot be in the past.");
    public static readonly Error CohortNotFound = Error.NotFound("certification.plan_cohort_not_found", "Cohort was not found.");
    public static readonly Error TraineeNotFound = Error.NotFound("certification.plan_trainee_not_found", "Trainee was not found.");
}

public sealed class CompetencyCourse
{
    private CompetencyCourse()
    {
    }

    internal CompetencyCourse(Guid courseId) => CourseId = courseId;

    public Guid CourseId { get; private set; }
}

/// <summary>A skill the institute tracks. Met by a valid certificate in a linked course, or by a staff sign-off.</summary>
public sealed partial class Competency : AggregateRoot<CompetencyId>
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;

    private readonly List<CompetencyCourse> _courses = [];

    private Competency()
    {
    }

    public string Code { get; private set; } = null!;
    public LocalizedText Name { get; private set; } = null!;
    public LocalizedText Description { get; private set; } = null!;
    public DateTimeOffset? RetiredAt { get; private set; }
    public IReadOnlyCollection<CompetencyCourse> Courses => _courses;

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static Result<Competency> Create(string code, LocalizedText name, LocalizedText description, IReadOnlyCollection<Guid> courseIds)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length > CodeMaxLength || !CodePattern().IsMatch(normalized))
        {
            return CompetencyErrors.InvalidCode;
        }

        var competency = new Competency { Id = new CompetencyId(Guid.CreateVersion7()), Code = normalized };
        competency.Update(name, description, courseIds);
        return competency;
    }

    /// <summary>Changes only what differs, so kept links are never deleted and re-added.</summary>
    public void Update(LocalizedText name, LocalizedText description, IReadOnlyCollection<Guid> courseIds)
    {
        Name = name;
        Description = description;
        _courses.RemoveAll(c => !courseIds.Contains(c.CourseId));
        _courses.AddRange(courseIds.Distinct().Where(id => _courses.All(c => c.CourseId != id)).Select(id => new CompetencyCourse(id)));
    }

    /// <summary>Stops it being added to plans. Plans that already use it keep it.</summary>
    public void Retire(DateTimeOffset now) => RetiredAt ??= now;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]*$")]
    private static partial Regex CodePattern();
}

/// <summary>Staff confirming a skill that no course shows, such as earlier experience.</summary>
public sealed class CompetencySignOff
{
    private CompetencySignOff()
    {
    }

    public Guid Id { get; private set; }
    public CompetencyId CompetencyId { get; private set; }
    public Guid TraineeId { get; private set; }
    public Guid SignedBy { get; private set; }
    public DateTimeOffset SignedAt { get; private set; }

    public static CompetencySignOff Create(CompetencyId competencyId, Guid traineeId, Guid signedBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CompetencyId = competencyId,
        TraineeId = traineeId,
        SignedBy = signedBy,
        SignedAt = now,
    };
}

public enum AttainmentSource { None, Course, SignOff }

/// <summary>Worked out on every read, so revoking a certificate takes effect at once.</summary>
public static class Attainment
{
    /// <summary>A course certificate wins over a sign-off, because it is the stronger evidence.</summary>
    public static AttainmentSource For(Competency competency, IReadOnlySet<Guid> certifiedCourses, IReadOnlySet<CompetencyId> signedOff)
    {
        if (competency.Courses.Any(c => certifiedCourses.Contains(c.CourseId)))
        {
            return AttainmentSource.Course;
        }

        return signedOff.Contains(competency.Id) ? AttainmentSource.SignOff : AttainmentSource.None;
    }
}

public sealed class PlanItem
{
    private PlanItem()
    {
    }

    internal PlanItem(CompetencyId competencyId, int position)
    {
        CompetencyId = competencyId;
        Position = position;
    }

    public CompetencyId CompetencyId { get; private set; }
    public int Position { get; internal set; }
}

/// <summary>A reusable list of competencies. Assigning it gives each trainee a plan to work through.</summary>
public sealed class LearningPlan : AggregateRoot<LearningPlanId>
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;
    public const int MaxCompetencies = 100;

    private readonly List<PlanItem> _items = [];

    private LearningPlan()
    {
    }

    public LocalizedText Name { get; private set; } = null!;
    public LocalizedText Description { get; private set; } = null!;
    public DateTimeOffset? RetiredAt { get; private set; }
    public IReadOnlyCollection<PlanItem> Items => _items;

    public IReadOnlyList<CompetencyId> OrderedCompetencies() => [.. _items.OrderBy(i => i.Position).Select(i => i.CompetencyId)];

    public static Result<LearningPlan> Create(LocalizedText name, LocalizedText description, IReadOnlyList<CompetencyId> competencies)
    {
        var plan = new LearningPlan { Id = new LearningPlanId(Guid.CreateVersion7()) };
        var updated = plan.Update(name, description, competencies);
        return updated.IsSuccess ? plan : updated.Error!;
    }

    /// <summary>Changes only what differs, so kept items are never deleted and re-added.</summary>
    public Result Update(LocalizedText name, LocalizedText description, IReadOnlyList<CompetencyId> competencies)
    {
        var wanted = competencies.Distinct().ToList();
        if (wanted.Count == 0 || wanted.Count > MaxCompetencies)
        {
            return CompetencyErrors.PlanEmpty;
        }

        Name = name;
        Description = description;
        _items.RemoveAll(i => !wanted.Contains(i.CompetencyId));

        for (var position = 0; position < wanted.Count; position++)
        {
            var item = _items.Find(i => i.CompetencyId == wanted[position]);
            if (item is null)
            {
                _items.Add(new PlanItem(wanted[position], position));
            }
            else
            {
                item.Position = position;
            }
        }

        return Result.Success();
    }

    /// <summary>Stops new assignments. Trainees who have the plan keep it.</summary>
    public void Retire(DateTimeOffset now) => RetiredAt ??= now;
}

public sealed class PlanAssignment : AggregateRoot<PlanAssignmentId>
{
    private PlanAssignment()
    {
    }

    public LearningPlanId PlanId { get; private set; }
    public Guid TraineeId { get; private set; }
    public DateOnly? DueOn { get; private set; }
    public Guid AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public static Result<PlanAssignment> Create(LearningPlan plan, Guid traineeId, DateOnly? dueOn, Guid assignedBy, DateTimeOffset now)
    {
        if (plan.RetiredAt is not null)
        {
            return CompetencyErrors.PlanRetired;
        }

        if (dueOn < DateOnly.FromDateTime(now.UtcDateTime))
        {
            return CompetencyErrors.DueInPast;
        }

        var assignment = new PlanAssignment
        {
            Id = new PlanAssignmentId(Guid.CreateVersion7()),
            PlanId = plan.Id,
            TraineeId = traineeId,
            DueOn = dueOn,
            AssignedBy = assignedBy,
            AssignedAt = now,
        };

        assignment.Raise(new LearningPlanAssigned(assignment.Id.Value, plan.Id.Value, traineeId, plan.Name.Ar, plan.Name.En));
        return assignment;
    }
}
