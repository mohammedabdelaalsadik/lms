using Lms.Modules.Assessment.Domain;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class QuizTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Title = new("اختبار", "Quiz");

    private static QuestionDraft Question(int points = 1) => new(
        QuestionKind.MultipleChoice, "س", "Q", points,
        [new OptionDraft("صح", "Right", true), new OptionDraft("خطأ", "Wrong", false)]);

    private static Quiz NewQuiz(int limit = 10, DateTimeOffset? closes = null, params QuestionDraft[] questions) =>
        Quiz.Create(Guid.NewGuid(), Title, limit, 2, null, closes, 1, questions).Value;

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 1, 0)]
    public void Rejects_an_invalid_schedule(int limit, int attempts, int weight) =>
        Assert.Equal(AssessmentErrors.InvalidSchedule, Quiz.Create(Guid.NewGuid(), Title, limit, attempts, null, null, weight, []).Error);

    [Fact]
    public void Rejects_a_closing_time_before_the_opening_time() =>
        Assert.Equal(AssessmentErrors.InvalidSchedule, Quiz.Create(Guid.NewGuid(), Title, 10, 1, Now, Now.AddHours(-1), 1, []).Error);

    [Fact]
    public void Cannot_publish_without_questions() =>
        Assert.Equal(AssessmentErrors.QuizNotReady, NewQuiz().Publish().Error);

    [Fact]
    public void Each_question_needs_exactly_one_correct_answer()
    {
        var twoCorrect = new QuestionDraft(QuestionKind.MultipleChoice, "س", "Q", 1, [new OptionDraft("أ", "A", true), new OptionDraft("ب", "B", true)]);

        Assert.Equal(AssessmentErrors.QuizNotReady, NewQuiz(10, null, twoCorrect).Publish().Error);
    }

    [Fact]
    public void Published_quiz_can_no_longer_change()
    {
        var quiz = NewQuiz(10, null, Question());
        quiz.Publish();

        Assert.Equal(AssessmentErrors.OnlyDraftCanChange, quiz.Update(Title, 10, 1, null, null, 1, []).Error);
    }

    [Fact]
    public void Open_only_within_its_window_and_once_published()
    {
        var quiz = Quiz.Create(Guid.NewGuid(), Title, 10, 1, Now, Now.AddHours(1), 1, [Question()]).Value;

        Assert.False(quiz.IsOpen(Now));

        quiz.Publish();

        Assert.False(quiz.IsOpen(Now.AddMinutes(-1)));
        Assert.True(quiz.IsOpen(Now));
        Assert.False(quiz.IsOpen(Now.AddHours(1)));
    }

    [Fact]
    public void Score_is_weighted_by_points_and_ignores_blanks()
    {
        var quiz = NewQuiz(10, null, Question(1), Question(3));
        var ordered = quiz.OrderedQuestions();
        var right = ordered[1].Options.First(o => o.IsCorrect);

        Assert.Equal(75m, quiz.Score(new Dictionary<QuestionId, string> { [ordered[1].Id] = right.Id.Value.ToString() }, Guid.Empty));
        Assert.Equal(0m, quiz.Score(new Dictionary<QuestionId, string>(), Guid.Empty));
    }

    [Fact]
    public void Attempt_ends_at_the_quiz_closing_time_when_that_comes_first()
    {
        var closes = Now.AddMinutes(5);

        Assert.Equal(closes, QuizAttempt.Start(NewQuiz(60, closes, Question()), Guid.NewGuid(), 1, Now).DeadlineAt);
    }

    [Fact]
    public void Answers_stop_at_the_deadline_and_late_finish_scores_what_was_saved()
    {
        var quiz = NewQuiz(10, null, Question());
        var question = quiz.OrderedQuestions()[0];
        var right = question.Options.First(o => o.IsCorrect);
        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);

        Assert.True(attempt.SaveAnswer(quiz, question.Id, right.Id.Value.ToString(), Now.AddMinutes(9)).IsSuccess);
        Assert.Equal(AssessmentErrors.AttemptOver, attempt.SaveAnswer(quiz, question.Id, right.Id.Value.ToString(), Now.AddMinutes(10)).Error);

        attempt.Finish(quiz, Now.AddHours(5));

        Assert.Equal(100m, attempt.ScorePercent);
        Assert.Equal(attempt.DeadlineAt, attempt.SubmittedAt);
    }

    [Fact]
    public void An_answer_must_belong_to_its_question()
    {
        var quiz = NewQuiz(10, null, Question(), Question());
        var ordered = quiz.OrderedQuestions();
        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);

        Assert.Equal(AssessmentErrors.InvalidAnswer, attempt.SaveAnswer(quiz, ordered[0].Id, ordered[1].Options.First().Id.Value.ToString(), Now).Error);
    }

    [Fact]
    public void Finishing_twice_keeps_the_first_result()
    {
        var quiz = NewQuiz(10, null, Question());
        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);
        attempt.Finish(quiz, Now.AddMinutes(1));

        attempt.Finish(quiz, Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), attempt.SubmittedAt);
    }
}

public class AssignmentTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Text = new("نص", "Text");
    private static readonly SubmittedFile File = new("key1", "a.pdf", "application/pdf", 5);

    private static Assignment NewAssignment() => Assignment.Create(Guid.NewGuid(), Text, Text, Due, 1).Value;

    [Fact]
    public void Flags_late_work_without_refusing_it()
    {
        Assert.False(AssignmentSubmission.Submit(NewAssignment(), Guid.NewGuid(), File, Due).IsLate);
        Assert.True(AssignmentSubmission.Submit(NewAssignment(), Guid.NewGuid(), File, Due.AddSeconds(1)).IsLate);
    }

    [Fact]
    public void Replacing_hands_back_the_old_file_until_reviewed()
    {
        var assignment = NewAssignment();
        var submission = AssignmentSubmission.Submit(assignment, Guid.NewGuid(), File, Due);

        Assert.Equal("key1", submission.Replace(assignment, File with { StorageKey = "key2" }, Due).Value);

        submission.Review("جيد", Due);

        Assert.Equal(AssessmentErrors.SubmissionClosed, submission.Replace(assignment, File, Due).Error);
    }
}

public class GradebookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Title = new("بند", "Item");
    private static readonly Guid Trainee = Guid.NewGuid();
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();

    private static (Gradebook Book, GradeItem Item) WithItem(int weight = 1)
    {
        var book = Gradebook.Create(Guid.NewGuid());
        return (book, book.AddItem(Guid.NewGuid(), GradeItemKind.Assignment, Title, weight));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Rejects_a_grade_outside_0_to_100(double percent)
    {
        var (book, item) = WithItem();

        Assert.Equal(AssessmentErrors.InvalidGrade, book.SetGrade(item.Id, Trainee, (decimal)percent).Error);
    }

    [Fact]
    public void Best_quiz_score_wins()
    {
        var (book, item) = WithItem();

        book.RecordQuizScore(item.Id, Trainee, 80);
        book.RecordQuizScore(item.Id, Trainee, 60);

        Assert.Equal(80, book.GradeOf(item.Id, Trainee));
    }

    [Fact]
    public void Grades_stay_hidden_until_published()
    {
        var (book, item) = WithItem();
        book.SetGrade(item.Id, Trainee, 90);

        Assert.Null(book.RunningTotal(Trainee));

        book.Publish(item.Id, Now);

        Assert.Equal(90, book.RunningTotal(Trainee));
    }

    [Fact]
    public void Total_follows_the_weights()
    {
        var (book, first) = WithItem(40);
        var second = book.AddItem(Guid.NewGuid(), GradeItemKind.Quiz, Title, 60);
        book.SetGrade(first.Id, Trainee, 50);
        book.SetGrade(second.Id, Trainee, 100);
        book.Publish(first.Id, Now);
        book.Publish(second.Id, Now);

        Assert.Equal(80, book.FinalGrade(Trainee));
    }

    [Fact]
    public void Final_grade_waits_for_every_item()
    {
        var (book, first) = WithItem();
        var second = book.AddItem(Guid.NewGuid(), GradeItemKind.Quiz, Title, 1);
        book.SetGrade(first.Id, Trainee, 100);
        book.Publish(first.Id, Now);
        book.SetGrade(second.Id, Trainee, 100);

        Assert.Null(book.FinalGrade(Trainee));

        book.Publish(second.Id, Now);

        Assert.Equal(100, book.FinalGrade(Trainee));
    }

    [Fact]
    public void Missing_grade_blocks_the_final_grade_even_when_published()
    {
        var (book, item) = WithItem();
        book.Publish(item.Id, Now);

        Assert.Null(book.FinalGrade(Trainee));
    }

    [Fact]
    public void Published_grade_cannot_be_edited_directly()
    {
        var (book, item) = WithItem();
        book.SetGrade(item.Id, Trainee, 70);
        book.Publish(item.Id, Now);

        Assert.Equal(AssessmentErrors.GradesPublished, book.SetGrade(item.Id, Trainee, 90).Error);
        Assert.Equal(AssessmentErrors.GradesPublished, book.ChangeWeight(item.Id, 5).Error);
    }

    [Fact]
    public void Missing_grade_can_be_entered_after_publishing_and_then_locks()
    {
        var (book, item) = WithItem();
        book.Publish(item.Id, Now);

        Assert.True(book.SetGrade(item.Id, Trainee, 55).IsSuccess);
        Assert.Equal(55, book.FinalGrade(Trainee));
        Assert.Equal(AssessmentErrors.GradesPublished, book.SetGrade(item.Id, Trainee, 90).Error);
    }

    private static (Gradebook Book, GradeItem Item, CorrectionId Id) WithCorrection()
    {
        var (book, item) = WithItem();
        book.SetGrade(item.Id, Trainee, 70);
        book.Publish(item.Id, Now);
        book.RequestCorrection(item.Id, Trainee, 85, "خطأ في الجمع", Teacher, Now);
        return (book, item, book.Corrections.Single().Id);
    }

    [Fact]
    public void Correction_needs_a_published_grade_and_a_reason()
    {
        var (book, item) = WithItem();

        Assert.Equal(AssessmentErrors.GradesNotPublished, book.RequestCorrection(item.Id, Trainee, 80, "x", Teacher, Now).Error);

        book.Publish(item.Id, Now);

        Assert.Equal(AssessmentErrors.ReasonRequired, book.RequestCorrection(item.Id, Trainee, 80, " ", Teacher, Now).Error);
        Assert.Equal(AssessmentErrors.NoGradeToCorrect, book.RequestCorrection(item.Id, Trainee, 80, "x", Teacher, Now).Error);
    }

    [Fact]
    public void Nothing_changes_until_someone_else_approves()
    {
        var (book, item, id) = WithCorrection();

        Assert.Equal(70, book.GradeOf(item.Id, Trainee));
        Assert.Equal(AssessmentErrors.CannotApproveOwn, book.ApproveCorrection(id, Teacher, Now).Error);
        Assert.True(book.ApproveCorrection(id, Manager, Now).IsSuccess);
        Assert.Equal(85, book.GradeOf(item.Id, Trainee));
    }

    [Fact]
    public void Approval_keeps_old_and_new_values_in_an_event()
    {
        var (book, _, id) = WithCorrection();
        book.ClearDomainEvents();

        book.ApproveCorrection(id, Manager, Now);

        var corrected = Assert.IsType<GradeCorrected>(Assert.Single(book.DomainEvents));
        Assert.Equal((70m, 85m, Teacher, Manager), (corrected.OldPercent, corrected.NewPercent, corrected.RequestedBy, corrected.ApprovedBy));
    }

    [Fact]
    public void Rejection_leaves_the_grade_and_a_decision_is_final()
    {
        var (book, item, id) = WithCorrection();

        book.RejectCorrection(id, Manager, Now);

        Assert.Equal(70, book.GradeOf(item.Id, Trainee));
        Assert.Equal(AssessmentErrors.CorrectionDecided, book.ApproveCorrection(id, Manager, Now).Error);
    }

    [Fact]
    public void Only_one_correction_can_wait_per_grade()
    {
        var (book, item, _) = WithCorrection();

        Assert.Equal(AssessmentErrors.CorrectionPending, book.RequestCorrection(item.Id, Trainee, 90, "x", Teacher, Now).Error);
    }
}

public class PassMarkTests
{
    private static readonly HashSet<ActivityId> None = [];

    [Theory]
    [InlineData(null, false)]
    [InlineData(59.99, false)]
    [InlineData(60.0, true)]
    public void Pass_mark_needs_a_final_grade_at_or_above_it(double? grade, bool expected) =>
        Assert.Equal(expected, new CompletionRule([], 0, 0, 60).IsMetBy(None, AttendanceTally.Empty, (decimal?)grade));

    [Fact]
    public void Failing_grade_does_not_complete_a_course_even_with_all_else_done()
    {
        var progress = CourseProgress.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        progress.RecordFinalGrade(40);
        progress.EvaluateCompletion(new CompletionRule([], 0, 0, 60), AttendanceTally.Empty, DateTimeOffset.UnixEpoch);

        Assert.Null(progress.CompletedAt);
    }

    [Fact]
    public void Corrected_grade_can_complete_a_course()
    {
        var progress = CourseProgress.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var rule = new CompletionRule([], 0, 0, 60);
        progress.RecordFinalGrade(40);
        progress.EvaluateCompletion(rule, AttendanceTally.Empty, DateTimeOffset.UnixEpoch);

        progress.RecordFinalGrade(75);
        progress.EvaluateCompletion(rule, AttendanceTally.Empty, DateTimeOffset.UnixEpoch);

        Assert.NotNull(progress.CompletedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_an_impossible_pass_mark(int mark) =>
        Assert.Equal(ContentErrors.InvalidPassMark, CourseContent.Create(Guid.NewGuid()).UpdateSettings(false, 0, 0, mark).Error);
}
