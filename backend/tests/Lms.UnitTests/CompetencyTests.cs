using Lms.Modules.Certification.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class CompetencyTests
{
    private static LocalizedText Text() => LocalizedText.Create("نص", "Text", 100).Value;

    private static Competency Skill(params Guid[] courses) => Competency.Create("SAFE-1", Text(), Text(), courses).Value;

    [Theory]
    [InlineData("bad code")]
    [InlineData("-X")]
    [InlineData("")]
    public void Bad_codes_are_refused(string code) =>
        Assert.Equal(CompetencyErrors.InvalidCode, Competency.Create(code, Text(), Text(), []).Error);

    [Fact]
    public void Codes_are_stored_in_capitals() => Assert.Equal("SAFE-1", Competency.Create(" safe-1 ", Text(), Text(), []).Value.Code);

    [Fact]
    public void Updating_links_keeps_existing_ones_and_drops_duplicates()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var skill = Skill(a, b);
        var kept = skill.Courses.First(x => x.CourseId == b);

        skill.Update(Text(), Text(), [b, c, c]);

        Assert.Equal(2, skill.Courses.Count);
        Assert.Contains(kept, skill.Courses);
        Assert.DoesNotContain(skill.Courses, x => x.CourseId == a);
    }

    [Fact]
    public void Certificate_meets_it_and_outranks_a_sign_off()
    {
        var course = Guid.NewGuid();
        var skill = Skill(course);

        Assert.Equal(AttainmentSource.Course, Attainment.For(skill, new HashSet<Guid> { course }, new HashSet<CompetencyId> { skill.Id }));
    }

    [Fact]
    public void Sign_off_meets_it_without_a_course()
    {
        var skill = Skill();

        Assert.Equal(AttainmentSource.SignOff, Attainment.For(skill, new HashSet<Guid>(), new HashSet<CompetencyId> { skill.Id }));
    }

    [Fact]
    public void A_certificate_in_another_course_does_not_count()
    {
        var skill = Skill(Guid.NewGuid());

        Assert.Equal(AttainmentSource.None, Attainment.For(skill, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<CompetencyId>()));
    }

    [Fact]
    public void A_plan_needs_a_competency() =>
        Assert.Equal(CompetencyErrors.PlanEmpty, LearningPlan.Create(Text(), Text(), []).Error);

    [Fact]
    public void Plan_update_follows_the_new_order()
    {
        var (a, b, c) = (new CompetencyId(Guid.NewGuid()), new CompetencyId(Guid.NewGuid()), new CompetencyId(Guid.NewGuid()));
        var plan = LearningPlan.Create(Text(), Text(), [a, b]).Value;

        plan.Update(Text(), Text(), [c, b, a]);

        Assert.Equal([c, b, a], plan.OrderedCompetencies());
    }

    [Fact]
    public void Retired_plans_cannot_be_assigned()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = LearningPlan.Create(Text(), Text(), [new CompetencyId(Guid.NewGuid())]).Value;
        plan.Retire(now);

        Assert.Equal(CompetencyErrors.PlanRetired, PlanAssignment.Create(plan, Guid.NewGuid(), null, Guid.NewGuid(), now).Error);
    }

    [Fact]
    public void Due_dates_cannot_be_in_the_past()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = LearningPlan.Create(Text(), Text(), [new CompetencyId(Guid.NewGuid())]).Value;
        var yesterday = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);

        Assert.Equal(CompetencyErrors.DueInPast, PlanAssignment.Create(plan, Guid.NewGuid(), yesterday, Guid.NewGuid(), now).Error);
    }

    [Fact]
    public void Assigning_raises_the_notification_event()
    {
        var plan = LearningPlan.Create(Text(), Text(), [new CompetencyId(Guid.NewGuid())]).Value;
        var trainee = Guid.NewGuid();

        var assignment = PlanAssignment.Create(plan, trainee, null, Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

        var raised = Assert.IsType<LearningPlanAssigned>(Assert.Single(assignment.DomainEvents));
        Assert.Equal(trainee, raised.TraineeId);
    }
}
