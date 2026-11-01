using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class CourseContentTests
{
    private static readonly LocalizedText Text = new("نص", "Text");

    private static (CourseContent Content, Section Section) WithSection()
    {
        var content = CourseContent.Create(Guid.NewGuid());
        return (content, content.AddSection(Text));
    }

    [Fact]
    public void Pages_need_text_in_both_languages()
    {
        var (content, section) = WithSection();

        Assert.Equal(ContentErrors.BodyRequired, content.AddPage(section.Id, Text, true, "نص", " ").Error);
    }

    [Theory]
    [InlineData("http://example.com/v")]
    [InlineData("javascript:alert(1)")]
    [InlineData("example.com/v")]
    [InlineData(null)]
    public void Video_links_must_be_https(string? url)
    {
        var (content, section) = WithSection();

        Assert.Equal(ContentErrors.InvalidVideoUrl, content.AddVideo(section.Id, Text, true, url).Error);
    }

    [Fact]
    public void Moving_swaps_with_the_neighbour_and_stops_at_the_edge()
    {
        var (content, section) = WithSection();
        content.AddVideo(section.Id, new LocalizedText("أ", "A"), false, "https://example.com/a");
        content.AddVideo(section.Id, new LocalizedText("ب", "B"), false, "https://example.com/b");
        var first = content.OrderedActivities()[0];

        content.MoveActivity(first.Id, MoveDirection.Down);
        Assert.Equal("B", content.OrderedActivities()[0].Title.En);

        content.MoveActivity(first.Id, MoveDirection.Down);
        Assert.Equal("A", content.OrderedActivities()[1].Title.En);
    }

    [Fact]
    public void Order_follows_sections_then_activities()
    {
        var (content, first) = WithSection();
        var second = content.AddSection(Text);
        content.AddVideo(second.Id, new LocalizedText("ب", "Second"), false, "https://example.com/b");
        content.AddVideo(first.Id, new LocalizedText("أ", "First"), false, "https://example.com/a");

        Assert.Equal("First", content.OrderedActivities()[0].Title.En);

        content.MoveSection(second.Id, MoveDirection.Up);

        Assert.Equal("Second", content.OrderedActivities()[0].Title.En);
    }

    [Theory]
    [InlineData(101, 10)]
    [InlineData(-1, 10)]
    [InlineData(80, 0)]
    [InlineData(0, -1)]
    public void Rejects_an_impossible_attendance_rule(int percent, int planned) =>
        Assert.Equal(ContentErrors.InvalidAttendanceRule, CourseContent.Create(Guid.NewGuid()).UpdateSettings(false, percent, planned).Error);

    [Fact]
    public void Removing_a_file_activity_hands_back_its_storage_key()
    {
        var (content, section) = WithSection();
        content.AddFile(section.Id, Text, false, new FileAsset("key", "a.pdf", "application/pdf", 10));

        Assert.Equal("key", content.RemoveActivity(content.OrderedActivities()[0].Id).Value);
    }
}

public class CompletionRuleTests
{
    private static readonly ActivityId A = new(Guid.NewGuid());
    private static readonly ActivityId B = new(Guid.NewGuid());
    private static readonly HashSet<ActivityId> None = [];
    private static readonly HashSet<ActivityId> Both = [A, B];

    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(7, 0, false)]
    [InlineData(7, 2, true)]
    [InlineData(0, 10, false)]
    public void Attendance_counts_against_planned_sessions_less_excused(int present, int excused, bool expected) =>
        Assert.Equal(expected, new CompletionRule([], 80, 10).AttendanceMet(new AttendanceTally(present, excused)));

    [Fact]
    public void Needs_every_required_activity()
    {
        var rule = new CompletionRule([A, B], 0, 0);

        Assert.False(rule.IsMetBy(new HashSet<ActivityId> { A }, AttendanceTally.Empty));
        Assert.True(rule.IsMetBy(Both, AttendanceTally.Empty));
    }

    [Fact]
    public void Needs_activities_and_attendance_together()
    {
        var rule = new CompletionRule([A, B], 50, 2);

        Assert.False(rule.IsMetBy(Both, AttendanceTally.Empty));
        Assert.False(rule.IsMetBy(None, new AttendanceTally(2, 0)));
        Assert.True(rule.IsMetBy(Both, new AttendanceTally(1, 0)));
    }

    [Fact]
    public void A_course_with_no_requirements_never_completes_by_itself() =>
        Assert.False(new CompletionRule([], 0, 0).IsMetBy(None, AttendanceTally.Empty));
}

public class CourseProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly ActivityId A = new(Guid.NewGuid());
    private static readonly ActivityId B = new(Guid.NewGuid());
    private static readonly ActivityId[] Order = [A, B];

    private static CourseProgress New() => CourseProgress.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Sequential_course_locks_later_activities()
    {
        var progress = New();

        Assert.Equal(ProgressErrors.ActivityLocked, progress.CompleteActivity(B, Order, true, Now).Error);
        Assert.True(progress.CompleteActivity(A, Order, true, Now).IsSuccess);
        Assert.True(progress.CompleteActivity(B, Order, true, Now).IsSuccess);
    }

    [Fact]
    public void Free_order_course_allows_any_activity() =>
        Assert.True(New().CompleteActivity(B, Order, false, Now).IsSuccess);

    [Fact]
    public void Completing_twice_counts_once()
    {
        var progress = New();

        progress.CompleteActivity(A, Order, false, Now);
        progress.CompleteActivity(A, Order, false, Now);

        Assert.Single(progress.Completions);
    }

    [Fact]
    public void Completion_is_recorded_once_and_survives_a_stricter_rule()
    {
        var progress = New();
        progress.CompleteActivity(A, Order, false, Now);

        progress.EvaluateCompletion(new CompletionRule([A], 0, 0), AttendanceTally.Empty, Now);
        progress.EvaluateCompletion(new CompletionRule([A], 0, 0), AttendanceTally.Empty, Now.AddDays(1));
        progress.EvaluateCompletion(new CompletionRule([A, B], 100, 5), AttendanceTally.Empty, Now.AddDays(2));

        Assert.Equal(Now, progress.CompletedAt);
        Assert.IsType<CourseCompleted>(Assert.Single(progress.DomainEvents));
    }
}

public class AttendanceSessionTests
{
    [Fact]
    public void Corrections_are_kept_as_events_with_old_and_new_status()
    {
        var trainee = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var session = AttendanceSession.Take(
            Guid.NewGuid(), new DateOnly(2026, 11, 1), Guid.NewGuid(), new Dictionary<Guid, AttendanceStatus> { [trainee] = AttendanceStatus.Absent });

        Assert.Empty(session.DomainEvents);

        session.Correct(new Dictionary<Guid, AttendanceStatus> { [trainee] = AttendanceStatus.Excused }, editor);

        var change = Assert.IsType<AttendanceCorrected>(Assert.Single(session.DomainEvents));
        Assert.Equal(AttendanceStatus.Absent, change.OldStatus);
        Assert.Equal(AttendanceStatus.Excused, change.NewStatus);
        Assert.Equal(editor, change.ChangedBy);
    }

    [Fact]
    public void Saving_the_same_status_logs_nothing()
    {
        var trainee = Guid.NewGuid();
        var entries = new Dictionary<Guid, AttendanceStatus> { [trainee] = AttendanceStatus.Present };
        var session = AttendanceSession.Take(Guid.NewGuid(), new DateOnly(2026, 11, 1), Guid.NewGuid(), entries);

        session.Correct(entries, Guid.NewGuid());

        Assert.Empty(session.DomainEvents);
    }
}
