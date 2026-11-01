using Lms.Modules.Assessment.Domain;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class EssayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Title = new("اختبار", "Quiz");

    private static QuestionDraft Essay(int points = 4) => new(QuestionKind.Essay, "ناقش", "Discuss", points, []);

    private static QuestionDraft Auto() => new(QuestionKind.ShortAnswer, "س", "Q", 6, [new OptionDraft("الرياض", "Riyadh", true)]);

    private static (Quiz Quiz, QuizAttempt Attempt) Start(params QuestionDraft[] drafts)
    {
        var quiz = Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, drafts).Value;
        quiz.Publish();
        return (quiz, QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now));
    }

    private static QuizQuestion Find(Quiz quiz, QuestionKind kind) => quiz.OrderedQuestions().First(q => q.Kind == kind);

    [Fact]
    public void Essay_with_a_prompt_is_complete() => Assert.True(Essay().IsComplete);

    [Fact]
    public void Answered_essay_waits_for_a_person()
    {
        var (quiz, attempt) = Start(Auto(), Essay());

        attempt.SaveAnswer(quiz, Find(quiz, QuestionKind.Essay).Id, "نص الإجابة", Now);
        attempt.Finish(quiz, Now.AddMinutes(1));

        Assert.True(attempt.AwaitingMarking);
    }

    [Fact]
    public void Unanswered_essay_needs_no_marking()
    {
        var (quiz, attempt) = Start(Auto(), Essay());

        attempt.Finish(quiz, Now.AddMinutes(1));

        Assert.False(attempt.AwaitingMarking);
        Assert.Equal(0m, attempt.ScorePercent);
    }

    [Fact]
    public void Marks_combine_with_automatic_points()
    {
        var (quiz, attempt) = Start(Auto(), Essay());
        var essay = Find(quiz, QuestionKind.Essay);

        attempt.SaveAnswer(quiz, Find(quiz, QuestionKind.ShortAnswer).Id, "Riyadh", Now);
        attempt.SaveAnswer(quiz, essay.Id, "نص", Now);
        attempt.Finish(quiz, Now.AddMinutes(1));
        attempt.MarkEssay(quiz, essay.Id, 2);

        Assert.False(attempt.AwaitingMarking);
        Assert.Equal(80m, attempt.ScorePercent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Marks_stay_within_the_question_points(decimal points)
    {
        var (quiz, attempt) = Start(Essay());
        var essay = Find(quiz, QuestionKind.Essay);

        attempt.SaveAnswer(quiz, essay.Id, "نص", Now);
        attempt.Finish(quiz, Now.AddMinutes(1));

        Assert.Equal(AssessmentErrors.InvalidGrade, attempt.MarkEssay(quiz, essay.Id, points).Error);
    }

    [Fact]
    public void An_unsubmitted_attempt_cannot_be_marked()
    {
        var (quiz, attempt) = Start(Essay());
        var essay = Find(quiz, QuestionKind.Essay);

        attempt.SaveAnswer(quiz, essay.Id, "نص", Now);

        Assert.Equal(AssessmentErrors.AttemptNotFound, attempt.MarkEssay(quiz, essay.Id, 1).Error);
    }

    [Fact]
    public void Two_essays_stay_pending_until_both_are_marked()
    {
        var (quiz, attempt) = Start(Essay(), Essay());
        var essays = quiz.OrderedQuestions();

        attempt.SaveAnswer(quiz, essays[0].Id, "أ", Now);
        attempt.SaveAnswer(quiz, essays[1].Id, "ب", Now);
        attempt.Finish(quiz, Now.AddMinutes(1));
        attempt.MarkEssay(quiz, essays[0].Id, 4);

        Assert.True(attempt.AwaitingMarking);

        attempt.MarkEssay(quiz, essays[1].Id, 4);

        Assert.False(attempt.AwaitingMarking);
        Assert.Equal(100m, attempt.ScorePercent);
    }
}

public class PasswordResetRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static User New() => User.Register(new ProtectedNationalId("c", "h", "0008"), new LocalizedText("أ", "A"), "a@b.co", "old", Role.Trainee, Language.Ar, Now);

    [Fact]
    public void Link_lasts_thirty_minutes()
    {
        var user = New();
        user.BeginPasswordReset("hash", Now);

        Assert.True(user.ResetIsValid(Now.AddMinutes(29)));
        Assert.False(user.ResetIsValid(Now.AddMinutes(30)));
    }

    [Fact]
    public void Link_works_once_and_clears_any_lockout()
    {
        var user = New();
        user.BeginPasswordReset("hash", Now);
        for (var i = 0; i < User.MaxFailedLogins; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.CompletePasswordReset("new");

        Assert.False(user.ResetIsValid(Now));
        Assert.False(user.IsLockedOut(Now));
        Assert.Equal("new", user.PasswordHash);
    }
}
