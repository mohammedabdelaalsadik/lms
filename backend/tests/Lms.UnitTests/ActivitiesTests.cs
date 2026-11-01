using Lms.Modules.Assessment.Domain;
using Lms.Modules.Community.Domain;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class BookAndLessonTests
{
    private static BookChapter Chapter() => new("ف", "C", "نص", "Text");

    private static LessonPage Page(params LessonChoice[] choices) => new("ص", "P", "نص", "Text", choices);

    [Fact]
    public void A_book_needs_a_chapter() => Assert.False(new BookDefinition([]).IsValid);

    [Fact]
    public void A_chapter_needs_both_languages() => Assert.False(new BookDefinition([new("ف", "C", "نص", " ")]).IsValid);

    [Fact]
    public void A_straight_lesson_ends_after_its_last_page() => Assert.True(new LessonDefinition([Page(), Page()]).IsValid);

    [Fact]
    public void Choices_must_point_at_real_pages() =>
        Assert.False(new LessonDefinition([Page(new LessonChoice("أ", "A", 9))]).IsValid);

    [Fact]
    public void A_lesson_that_only_loops_is_refused() =>
        Assert.False(new LessonDefinition([Page(new LessonChoice("أ", "A", 1)), Page(new LessonChoice("ب", "B", 0))]).IsValid);

    [Fact]
    public void One_route_to_the_end_is_enough() =>
        Assert.True(new LessonDefinition([Page(new LessonChoice("أ", "A", 0), new LessonChoice("ب", "B", null))]).IsValid);

    [Fact]
    public void A_book_stays_with_the_activity_through_the_bin()
    {
        var content = CourseContent.Create(Guid.NewGuid());
        var title = LocalizedText.Create("ن", "T", 100).Value;
        var section = content.AddSection(title);
        content.AddStructured(ActivityKind.Book, section.Id, title, true, FormCodec.Write(new BookDefinition([Chapter()])));
        var activity = content.OrderedActivities()[0];
        var (snapshot, sectionId) = content.SnapshotActivity(activity.Id)!.Value;

        content.RemoveActivity(activity.Id);
        content.RestoreActivity(snapshot, sectionId);

        Assert.Single(FormCodec.ReadBook(content.OrderedActivities()[0].StructureJson)!.Chapters);
    }
}

public class WikiTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Editing_from_an_old_version_is_refused()
    {
        var (page, _) = WikiPage.Create(Guid.NewGuid(), "ص", "أ", Guid.NewGuid(), Now);
        page.Edit("ص", "ب", 1, Guid.NewGuid(), Now);

        Assert.Equal(WikiErrors.Stale, page.Edit("ص", "ج", 1, Guid.NewGuid(), Now).Error);
        Assert.Equal("ب", page.Body);
    }

    [Fact]
    public void Each_save_adds_a_revision()
    {
        var (page, first) = WikiPage.Create(Guid.NewGuid(), "ص", "أ", Guid.NewGuid(), Now);

        var second = page.Edit("ص", "ب", 1, Guid.NewGuid(), Now).Value;

        Assert.Equal((1, 2), (first.Version, second.Version));
    }

    [Fact]
    public void Going_back_adds_to_the_history()
    {
        var (page, first) = WikiPage.Create(Guid.NewGuid(), "ص", "أ", Guid.NewGuid(), Now);
        page.Edit("ص", "ب", 1, Guid.NewGuid(), Now);

        var restored = page.Restore(first, Guid.NewGuid(), Now);

        Assert.Equal("أ", page.Body);
        Assert.Equal(3, restored.Version);
    }
}

public class WorkshopTests
{
    private static readonly List<Guid> Five = [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())];

    private static Workshop Sample()
    {
        var text = LocalizedText.Create("أ", "A", 100).Value;
        return Workshop.Create(Guid.NewGuid(), text, text, 2, 10).Value;
    }

    [Fact]
    public void Nobody_reviews_their_own_work() =>
        Assert.DoesNotContain(ReviewAllocator.Deal(Five, 3, new Random(1)), p => p.Author == p.Reviewer);

    [Fact]
    public void Nobody_gets_the_same_work_twice()
    {
        var pairs = ReviewAllocator.Deal(Five, 4, new Random(2));

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public void Everyone_gives_and_gets_the_same_number()
    {
        var pairs = ReviewAllocator.Deal(Five, 2, new Random(3));

        Assert.All(Five, id =>
        {
            Assert.Equal(2, pairs.Count(p => p.Author == id));
            Assert.Equal(2, pairs.Count(p => p.Reviewer == id));
        });
    }

    [Fact]
    public void Two_people_review_each_other_whatever_the_setting()
    {
        var pairs = ReviewAllocator.Deal(Five.Take(2).ToList(), 5, new Random(4));

        Assert.Equal(2, pairs.Count);
    }

    [Fact]
    public void The_grade_is_the_average_of_finished_reviews() =>
        Assert.Equal(75m, Workshop.Average([70m, 80m, null]));

    [Fact]
    public void No_finished_review_means_no_score() => Assert.Null(Workshop.Average([null]));

    [Fact]
    public void Reviews_need_two_submissions() => Assert.Equal(WorkshopErrors.TooFewSubmissions, Sample().StartReviews(1).Error);

    [Fact]
    public void It_runs_in_order()
    {
        var workshop = Sample();
        Assert.Equal(WorkshopErrors.AlreadyClosed, workshop.Close().Error);

        workshop.StartReviews(2);
        workshop.Close();

        Assert.Equal(WorkshopStatus.Closed, workshop.Status);
        Assert.Equal(WorkshopErrors.ReviewClosed, workshop.StartReviews(2).Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Scores_run_from_0_to_100(int score) =>
        Assert.Equal(WorkshopErrors.InvalidScore, WorkshopReview.Assign(new WorkshopId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid()).Record(score, null, DateTimeOffset.UtcNow).Error);
}
