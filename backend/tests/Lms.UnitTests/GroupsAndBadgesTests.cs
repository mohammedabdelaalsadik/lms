using Lms.Modules.Certification.Domain;
using Lms.Modules.Community.Domain;
using Lms.Modules.Enrollment.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class GroupTests
{
    [Fact]
    public void Dealing_keeps_sizes_within_one()
    {
        var cohort = Guid.NewGuid();
        CohortGroup[] groups = [CohortGroup.Create(cohort, "أ"), CohortGroup.Create(cohort, "ب"), CohortGroup.Create(cohort, "ج")];

        CohortGroup.Deal(groups, [.. Enumerable.Range(0, 10).Select(_ => Guid.NewGuid())]);

        Assert.Equal(10, groups.Sum(g => g.Members.Count));
        Assert.True(groups.Max(g => g.Members.Count) - groups.Min(g => g.Members.Count) <= 1);
    }

    [Fact]
    public void Dealing_replaces_earlier_members()
    {
        var group = CohortGroup.Create(Guid.NewGuid(), "أ");
        var old = Guid.NewGuid();
        group.Add(old);

        CohortGroup.Deal([group], [Guid.NewGuid()]);

        Assert.DoesNotContain(group.Members, m => m.TraineeId == old);
    }

    [Fact]
    public void Adding_twice_counts_once()
    {
        var group = CohortGroup.Create(Guid.NewGuid(), "أ");
        var person = Guid.NewGuid();

        group.Add(person);
        group.Add(person);

        Assert.Single(group.Members);
    }

    [Fact]
    public void Group_topic_remembers_its_group()
    {
        var group = Guid.NewGuid();

        Assert.Equal(group, ForumTopic.Start(Guid.NewGuid(), Guid.NewGuid(), "ع", "ن", DateTimeOffset.UtcNow, group).Value.GroupId);
    }
}

public class BadgeTests
{
    private static readonly LocalizedText Text = new("أ", "A");

    [Fact]
    public void Icon_must_come_from_the_list() =>
        Assert.Equal(BadgeErrors.InvalidIcon, Badge.Create(Guid.NewGuid(), Text, Text, "<script>").Error);

    [Fact]
    public void Retiring_keeps_the_first_date()
    {
        var now = DateTimeOffset.UtcNow;
        var badge = Badge.Create(Guid.NewGuid(), Text, Text, "star").Value;

        badge.Retire(now);
        badge.Retire(now.AddDays(1));

        Assert.Equal(now, badge.RetiredAt);
    }

    [Fact]
    public void Award_carries_the_course_so_revoking_can_find_it()
    {
        var course = Guid.NewGuid();
        var badge = Badge.Create(course, Text, Text, "star").Value;

        Assert.Equal(course, BadgeAward.Create(badge, Guid.NewGuid(), DateTimeOffset.UtcNow).CourseId);
    }
}
