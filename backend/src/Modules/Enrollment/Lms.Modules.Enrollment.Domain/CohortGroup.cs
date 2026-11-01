using Lms.SharedKernel;

namespace Lms.Modules.Enrollment.Domain;

public readonly record struct CohortGroupId(Guid Value);

public sealed class GroupMember
{
    private GroupMember()
    {
    }

    public Guid CohortId { get; private set; }
    public Guid TraineeId { get; private set; }

    internal static GroupMember Create(Guid cohortId, Guid traineeId) => new() { CohortId = cohortId, TraineeId = traineeId };
}

/// <summary>A smaller team inside a cohort. A trainee belongs to at most one group per cohort.</summary>
public sealed class CohortGroup : AggregateRoot<CohortGroupId>
{
    public const int NameMaxLength = 100;

    private readonly List<GroupMember> _members = [];

    private CohortGroup()
    {
    }

    public Guid CohortId { get; private set; }
    public string Name { get; private set; } = null!;

    public IReadOnlyCollection<GroupMember> Members => _members;

    public static CohortGroup Create(Guid cohortId, string name) => new()
    {
        Id = new CohortGroupId(Guid.CreateVersion7()),
        CohortId = cohortId,
        Name = name.Trim(),
    };

    public void Rename(string name) => Name = name.Trim();

    public void Add(Guid traineeId)
    {
        if (!_members.Exists(m => m.TraineeId == traineeId))
        {
            _members.Add(GroupMember.Create(CohortId, traineeId));
        }
    }

    public void Remove(Guid traineeId) => _members.RemoveAll(m => m.TraineeId == traineeId);

    public void Clear() => _members.Clear();

    /// <summary>Deals trainees out evenly, so group sizes differ by at most one.</summary>
    public static void Deal(IReadOnlyList<CohortGroup> groups, IReadOnlyList<Guid> trainees)
    {
        foreach (var group in groups)
        {
            group.Clear();
        }

        for (var i = 0; i < trainees.Count; i++)
        {
            groups[i % groups.Count].Add(trainees[i]);
        }
    }
}
