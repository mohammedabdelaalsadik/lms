using Lms.SharedKernel;
using System.Security.Cryptography;

namespace Lms.Modules.Assessment.Domain;

public readonly record struct QuizId(Guid Value);

public readonly record struct QuestionId(Guid Value);

public readonly record struct OptionId(Guid Value);

public readonly record struct AttemptId(Guid Value);

public enum QuizStatus { Draft, Published }

public enum QuestionKind { MultipleChoice, TrueFalse, MultipleResponse, ShortAnswer, Numeric, Matching, Essay }

public static class AssessmentErrors
{
    public static readonly Error CohortNotFound = Error.NotFound("assessment.cohort_not_found", "Cohort was not found.");
    public static readonly Error QuizNotFound = Error.NotFound("assessment.quiz_not_found", "Quiz was not found.");
    public static readonly Error AttemptNotFound = Error.NotFound("assessment.attempt_not_found", "Attempt was not found.");
    public static readonly Error AssignmentNotFound = Error.NotFound("assessment.assignment_not_found", "Assignment was not found.");
    public static readonly Error SubmissionNotFound = Error.NotFound("assessment.submission_not_found", "Submission was not found.");
    public static readonly Error ItemNotFound = Error.NotFound("assessment.item_not_found", "Gradebook item was not found.");
    public static readonly Error CorrectionNotFound = Error.NotFound("assessment.correction_not_found", "Correction request was not found.");
    public static readonly Error Forbidden = Error.Forbidden("assessment.forbidden", "You cannot do this for this cohort.");
    public static readonly Error NotEnrolled = Error.Forbidden("assessment.not_enrolled", "You are not enrolled in this cohort.");
    public static readonly Error NotStarted = Error.Conflict("assessment.not_started", "This cohort has not started yet.");
    public static readonly Error InvalidSchedule = Error.Validation("assessment.invalid_schedule", "Check the time limit, attempts, weight and dates.");
    public static readonly Error QuizNotReady = Error.Validation("assessment.quiz_not_ready", "Every question needs its answers filled in, and a draw cannot exceed the number of questions.");
    public static readonly Error OnlyDraftCanChange = Error.Conflict("assessment.only_draft_can_change", "Only a draft quiz can be changed.");
    public static readonly Error QuizClosed = Error.Conflict("assessment.quiz_closed", "This quiz is not open.");
    public static readonly Error NoAttemptsLeft = Error.Conflict("assessment.no_attempts_left", "You have used all your attempts.");
    public static readonly Error AttemptOver = Error.Conflict("assessment.attempt_over", "This attempt has ended.");
    public static readonly Error InvalidAnswer = Error.Validation("assessment.invalid_answer", "This answer does not belong to the question.");
    public static readonly Error MarkingPending = Error.Conflict("assessment.marking_pending", "Mark the essays before publishing these grades.");
    public static readonly Error QuestionIncomplete = Error.Validation("assessment.question_incomplete", "Fill in the answers for this question.");
    public static readonly Error BankQuestionNotFound = Error.NotFound("assessment.bank_question_not_found", "Question was not found.");
    public static readonly Error ImportInvalid = Error.Validation("assessment.import_invalid", "This is not a Moodle question XML file.");
    public static readonly Error GradesPublished = Error.Conflict("assessment.grades_published", "Grades are already published for this item.");
    public static readonly Error GradesNotPublished = Error.Conflict("assessment.grades_not_published", "A correction applies only to published grades.");
    public static readonly Error InvalidGrade = Error.Validation("assessment.invalid_grade", "A grade must be between 0 and 100.");
    public static readonly Error InvalidWeight = Error.Validation("assessment.invalid_weight", "Weight must be greater than zero.");
    public static readonly Error ReasonRequired = Error.Validation("assessment.reason_required", "A reason is required.");
    public static readonly Error CorrectionPending = Error.Conflict("assessment.correction_pending", "A correction is already waiting for this grade.");
    public static readonly Error CorrectionDecided = Error.Conflict("assessment.correction_decided", "This correction has already been decided.");
    public static readonly Error CannotApproveOwn = Error.Forbidden("assessment.cannot_approve_own", "A correction must be approved by someone else.");
    public static readonly Error NoGradeToCorrect = Error.Conflict("assessment.no_grade_to_correct", "This trainee has no grade for this item.");
    public static readonly Error SubmissionClosed = Error.Conflict("assessment.submission_closed", "This assignment no longer accepts submissions.");
}

public sealed class QuizOption : Entity<OptionId>
{
    public const int TextMaxLength = 500;

    private QuizOption()
    {
    }

    public LocalizedText Text { get; private set; } = null!;
    public bool IsCorrect { get; private set; }
    public int Position { get; private set; }
    public string? MatchAr { get; private set; }
    public string? MatchEn { get; private set; }

    internal static QuizOption Create(LocalizedText text, OptionDraft draft, int position) => new()
    {
        Id = new OptionId(Guid.CreateVersion7()),
        Text = text,
        IsCorrect = draft.IsCorrect,
        Position = position,
        MatchAr = draft.MatchAr?.Trim(),
        MatchEn = draft.MatchEn?.Trim(),
    };
}

public sealed class QuizQuestion : Entity<QuestionId>
{
    public const int TextMaxLength = 1000;
    public const int MaxEssayLength = 8000;

    private readonly List<QuizOption> _options = [];

    private QuizQuestion()
    {
    }

    public QuestionKind Kind { get; private set; }
    public LocalizedText Text { get; private set; } = null!;
    public int Points { get; private set; }
    public int Position { get; private set; }
    public decimal? Tolerance { get; private set; }

    public IReadOnlyCollection<QuizOption> Options => _options;

    internal bool IsReady => new QuestionDraft(
        Kind,
        Text.Ar,
        Text.En,
        Points,
        [.. _options.OrderBy(o => o.Position).Select(o => new OptionDraft(o.Text.Ar, o.Text.En, o.IsCorrect, o.MatchAr, o.MatchEn))],
        Tolerance).IsComplete;

    /// <summary>Marks are all or nothing. A blank or malformed response is simply wrong.</summary>
    public bool IsCorrect(string? response, Guid seed)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        switch (Kind)
        {
            case QuestionKind.MultipleChoice or QuestionKind.TrueFalse:
                return _options.Exists(o => o.IsCorrect && o.Id.Value.ToString() == response.Trim().ToLowerInvariant());

            case QuestionKind.MultipleResponse:
                var chosen = response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
                return chosen.SetEquals(_options.Where(o => o.IsCorrect).Select(o => o.Id.Value.ToString()));

            case QuestionKind.ShortAnswer:
                var typed = AnswerText.Normalize(response);
                return _options.Exists(o => AnswerText.Normalize(o.Text.Ar) == typed || AnswerText.Normalize(o.Text.En) == typed);

            case QuestionKind.Numeric:
                return AnswerText.ParseNumber(response) is { } number
                    && AnswerText.ParseNumber(_options[0].Text.En) is { } target
                    && Math.Abs(number - target) <= (Tolerance ?? 0);

            case QuestionKind.Matching:
                var pairs = ParsePairs(response);
                return pairs.Count == _options.Count && _options.TrueForAll(o => pairs.GetValueOrDefault(o.Id.Value.ToString()) == AnswerText.SecretKey(seed, o.Id.Value));

            default:
                return false;
        }
    }

    internal bool Accepts(string response, Guid seed)
    {
        const int MaxTextLength = 500;

        switch (Kind)
        {
            case QuestionKind.MultipleChoice or QuestionKind.TrueFalse:
                return _options.Exists(o => o.Id.Value.ToString() == response.Trim().ToLowerInvariant());

            case QuestionKind.MultipleResponse:
                var ids = _options.Select(o => o.Id.Value.ToString()).ToHashSet();
                return response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(x => ids.Contains(x.ToLowerInvariant()));

            case QuestionKind.Matching:
                var keys = _options.ToDictionary(o => o.Id.Value.ToString(), o => AnswerText.SecretKey(seed, o.Id.Value));
                var known = keys.Values.ToHashSet();
                return ParsePairs(response).All(p => keys.ContainsKey(p.Key) && known.Contains(p.Value));

            case QuestionKind.Essay:
                return response.Length <= MaxEssayLength;

            default:
                return response.Length <= MaxTextLength;
        }
    }

    private static Dictionary<string, string> ParsePairs(string response)
    {
        var pairs = new Dictionary<string, string>();

        foreach (var pair in response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':');

            if (parts.Length == 2)
            {
                pairs[parts[0].ToLowerInvariant()] = parts[1].ToLowerInvariant();
            }
        }

        return pairs;
    }

    internal static Result<QuizQuestion> Create(QuestionDraft draft, int position)
    {
        var text = LocalizedText.Create(draft.TextAr, draft.TextEn, TextMaxLength);
        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        var question = new QuizQuestion
        {
            Id = new QuestionId(Guid.CreateVersion7()),
            Kind = draft.Kind,
            Text = text.Value,
            Points = draft.Points,
            Position = position,
            Tolerance = draft.Tolerance,
        };

        foreach (var (option, index) in draft.Options.Select((o, i) => (o, i)))
        {
            var optionText = LocalizedText.Create(option.TextAr, option.TextEn, QuizOption.TextMaxLength);
            if (!optionText.IsSuccess)
            {
                return optionText.Error!;
            }

            question._options.Add(QuizOption.Create(optionText.Value, option, index));
        }

        return question;
    }
}

public sealed class Quiz : AggregateRoot<QuizId>
{
    public const int TitleMaxLength = 200;

    private readonly List<QuizQuestion> _questions = [];

    private Quiz()
    {
    }

    public Guid CohortId { get; private set; }
    public LocalizedText Title { get; private set; } = null!;
    public int TimeLimitMinutes { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? OpensAt { get; private set; }
    public DateTimeOffset? ClosesAt { get; private set; }
    public int Weight { get; private set; }
    public QuizStatus Status { get; private set; }

    /// <summary>When set, each attempt gets this many questions drawn at random.</summary>
    public int? DrawCount { get; private set; }

    /// <summary>When set, questions and options appear in a different order for each attempt.</summary>
    public bool Shuffle { get; private set; }

    /// <summary>When set, the quiz opens only in Safe Exam Browser with this Config Key.</summary>
    public string? ExamKey { get; private set; }

    public IReadOnlyCollection<QuizQuestion> Questions => _questions;

    public static Result<Quiz> Create(
        Guid cohortId,
        LocalizedText title,
        int timeLimitMinutes,
        int maxAttempts,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        int weight,
        IReadOnlyList<QuestionDraft> questions,
        int? drawCount = null,
        bool shuffle = false)
    {
        var quiz = new Quiz { Id = new QuizId(Guid.CreateVersion7()), CohortId = cohortId, Status = QuizStatus.Draft };
        var result = quiz.Update(title, timeLimitMinutes, maxAttempts, opensAt, closesAt, weight, questions, drawCount, shuffle);
        return result.IsSuccess ? quiz : result.Error!;
    }

    public Result Update(
        LocalizedText title,
        int timeLimitMinutes,
        int maxAttempts,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        int weight,
        IReadOnlyList<QuestionDraft> questions,
        int? drawCount = null,
        bool shuffle = false)
    {
        if (Status != QuizStatus.Draft)
        {
            return AssessmentErrors.OnlyDraftCanChange;
        }

        if (timeLimitMinutes <= 0 || maxAttempts <= 0 || weight <= 0 || closesAt <= opensAt || drawCount <= 0)
        {
            return AssessmentErrors.InvalidSchedule;
        }

        var built = new List<QuizQuestion>();

        foreach (var (draft, index) in questions.Select((q, i) => (q, i)))
        {
            var question = QuizQuestion.Create(draft, index);
            if (!question.IsSuccess)
            {
                return question.Error!;
            }

            built.Add(question.Value);
        }

        Title = title;
        TimeLimitMinutes = timeLimitMinutes;
        MaxAttempts = maxAttempts;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        Weight = weight;
        DrawCount = drawCount;
        Shuffle = shuffle;
        _questions.Clear();
        _questions.AddRange(built);
        return Result.Success();
    }

    public Result RequireExamBrowser(string? key)
    {
        var normalized = SafeExam.Normalize(key);
        if (!SafeExam.IsValidKey(normalized))
        {
            return SafeExam.InvalidKey;
        }

        ExamKey = normalized;
        return Result.Success();
    }

    public Result Publish()
    {
        if (Status != QuizStatus.Draft)
        {
            return AssessmentErrors.OnlyDraftCanChange;
        }

        if (_questions.Count == 0 || DrawCount > _questions.Count || !_questions.TrueForAll(q => q.IsReady))
        {
            return AssessmentErrors.QuizNotReady;
        }

        Status = QuizStatus.Published;
        return Result.Success();
    }

    public bool IsOpen(DateTimeOffset now) =>
        Status == QuizStatus.Published && (OpensAt is null || now >= OpensAt) && (ClosesAt is null || now < ClosesAt);

    public IReadOnlyList<QuizQuestion> OrderedQuestions() => [.. _questions.OrderBy(q => q.Position)];

    /// <summary>Share of points earned. Unanswered questions earn nothing.</summary>
    public decimal Score(
        IReadOnlyDictionary<QuestionId, string> answers,
        Guid seed,
        IReadOnlyCollection<QuizQuestion>? only = null,
        IReadOnlyDictionary<QuestionId, decimal>? marks = null)
    {
        var asked = only ?? _questions;
        var total = asked.Sum(q => q.Points);
        var earned = asked
            .Where(q => answers.TryGetValue(q.Id, out var response) && q.IsCorrect(response, seed))
            .Sum(q => (decimal)q.Points) + asked.Sum(q => marks?.GetValueOrDefault(q.Id) ?? 0);

        return total == 0 ? 0 : Math.Round(earned * 100m / total, 2);
    }
}

public sealed class AttemptAnswer
{
    private AttemptAnswer()
    {
    }

    public QuestionId QuestionId { get; private set; }
    public string Response { get; private set; } = null!;
    public decimal? AwardedPoints { get; private set; }

    internal void Award(decimal points) => AwardedPoints = points;

    internal static AttemptAnswer Create(QuestionId questionId, string response) => new() { QuestionId = questionId, Response = response };

    internal void Change(string response) => Response = response;
}

/// <summary>One sitting of a quiz. The server's clock decides when it ends.</summary>
public sealed class QuizAttempt : AggregateRoot<AttemptId>
{
    private readonly List<AttemptAnswer> _answers = [];

    private QuizAttempt()
    {
    }

    public QuizId QuizId { get; private set; }
    public Guid TraineeId { get; private set; }
    public int Number { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset DeadlineAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public decimal? ScorePercent { get; private set; }

    /// <summary>Stays on the server. It fixes this attempt's order and its matching labels.</summary>
    public Guid Seed { get; private set; }

    /// <summary>The drawn questions. Empty means the whole quiz.</summary>
    public string? QuestionIds { get; private set; }

    /// <summary>True while a submitted essay still needs a person. The score counts for nothing until then.</summary>
    public bool AwaitingMarking { get; private set; }

    public IReadOnlyCollection<AttemptAnswer> Answers => _answers;

    public bool IsFinished => SubmittedAt is not null;

    public static QuizAttempt Start(Quiz quiz, Guid traineeId, int number, DateTimeOffset now)
    {
        var deadline = now.AddMinutes(quiz.TimeLimitMinutes);
        var drawn = quiz.DrawCount is { } count && count < quiz.Questions.Count
            ? string.Join(',', Draw(quiz, count))
            : null;

        return new QuizAttempt
        {
            Seed = Guid.NewGuid(),
            QuestionIds = drawn,
            Id = new AttemptId(Guid.CreateVersion7()),
            QuizId = quiz.Id,
            TraineeId = traineeId,
            Number = number,
            StartedAt = now,
            DeadlineAt = quiz.ClosesAt < deadline ? quiz.ClosesAt.Value : deadline,
        };
    }

    private static IEnumerable<string> Draw(Quiz quiz, int count)
    {
        var questions = quiz.Questions.ToArray();
        RandomNumberGenerator.Shuffle<QuizQuestion>(questions);
        return questions.Take(count).Select(q => q.Id.Value.ToString());
    }

    public IReadOnlyList<QuizQuestion> QuestionsFor(Quiz quiz)
    {
        var ordered = quiz.OrderedQuestions();

        if (!string.IsNullOrEmpty(QuestionIds))
        {
            var drawn = QuestionIds.Split(',').ToHashSet();
            ordered = [.. ordered.Where(q => drawn.Contains(q.Id.Value.ToString()))];
        }

        return quiz.Shuffle ? [.. ordered.OrderBy(q => AnswerText.SecretKey(Seed, q.Id.Value), StringComparer.Ordinal)] : ordered;
    }

    public bool HasExpired(DateTimeOffset now) => now >= DeadlineAt;

    public Result SaveAnswer(Quiz quiz, QuestionId questionId, string? response, DateTimeOffset now)
    {
        if (IsFinished || HasExpired(now))
        {
            return AssessmentErrors.AttemptOver;
        }

        var question = QuestionsFor(quiz).FirstOrDefault(q => q.Id == questionId);

        if (question is null || (!string.IsNullOrWhiteSpace(response) && !question.Accepts(response, Seed)))
        {
            return AssessmentErrors.InvalidAnswer;
        }

        var existing = _answers.Find(a => a.QuestionId == questionId);

        if (string.IsNullOrWhiteSpace(response))
        {
            if (existing is not null)
            {
                _answers.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _answers.Add(AttemptAnswer.Create(questionId, response));
        }
        else
        {
            existing.Change(response);
        }

        return Result.Success();
    }

    /// <summary>Scores what was saved. A finish after the deadline is dated at the deadline.</summary>
    public void Finish(Quiz quiz, DateTimeOffset now)
    {
        if (IsFinished)
        {
            return;
        }

        SubmittedAt = now < DeadlineAt ? now : DeadlineAt;
        Rescore(quiz);
        AwaitingMarking = QuestionsFor(quiz).Any(q => q.Kind == QuestionKind.Essay && _answers.Exists(a => a.QuestionId == q.Id));
    }

    public Result MarkEssay(Quiz quiz, QuestionId questionId, decimal points)
    {
        var question = QuestionsFor(quiz).FirstOrDefault(q => q.Id == questionId && q.Kind == QuestionKind.Essay);
        var answer = _answers.Find(a => a.QuestionId == questionId);

        if (!IsFinished || question is null || answer is null)
        {
            return AssessmentErrors.AttemptNotFound;
        }

        if (points < 0 || points > question.Points)
        {
            return AssessmentErrors.InvalidGrade;
        }

        answer.Award(points);
        Rescore(quiz);

        var essays = QuestionsFor(quiz).Where(q => q.Kind == QuestionKind.Essay).Select(q => q.Id).ToHashSet();
        AwaitingMarking = _answers.Exists(a => essays.Contains(a.QuestionId) && a.AwardedPoints is null);
        return Result.Success();
    }

    private void Rescore(Quiz quiz) => ScorePercent = quiz.Score(
        _answers.ToDictionary(a => a.QuestionId, a => a.Response),
        Seed,
        QuestionsFor(quiz),
        _answers.Where(a => a.AwardedPoints is not null).ToDictionary(a => a.QuestionId, a => a.AwardedPoints!.Value));
}
