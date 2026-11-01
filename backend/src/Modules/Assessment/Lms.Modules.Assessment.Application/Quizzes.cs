using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public sealed record StaffOption(Guid Id, TextDto Text, bool IsCorrect, string? MatchAr, string? MatchEn);

public sealed record StaffQuestion(Guid Id, QuestionKind Kind, TextDto Text, int Points, decimal? Tolerance, IReadOnlyList<StaffOption> Options);

public sealed record QuizResponse(
    Guid Id,
    TextDto Title,
    int TimeLimitMinutes,
    int MaxAttempts,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int Weight,
    QuizStatus Status,
    int? DrawCount,
    bool Shuffle,
    IReadOnlyList<StaffQuestion> Questions,
    string? ExamKey = null);

internal static class QuizMapping
{
    public static QuizResponse ToResponse(this Quiz q) => new(
        q.Id.Value,
        new TextDto(q.Title.Ar, q.Title.En),
        q.TimeLimitMinutes,
        q.MaxAttempts,
        q.OpensAt,
        q.ClosesAt,
        q.Weight,
        q.Status,
        q.DrawCount,
        q.Shuffle,
        [.. q.OrderedQuestions().Select(x => new StaffQuestion(
            x.Id.Value,
            x.Kind,
            new TextDto(x.Text.Ar, x.Text.En),
            x.Points,
            x.Tolerance,
            [.. x.Options.OrderBy(o => o.Position).Select(o => new StaffOption(o.Id.Value, new TextDto(o.Text.Ar, o.Text.En), o.IsCorrect, o.MatchAr, o.MatchEn))]))],
        q.ExamKey);
}

public sealed record ListQuizzesQuery(Guid CohortId) : IQuery<IReadOnlyList<QuizResponse>>;

internal sealed class ListQuizzesHandler(AssessmentAccess access, IQuizRepository quizzes) : IRequestHandler<ListQuizzesQuery, IReadOnlyList<QuizResponse>>
{
    public async Task<Result<IReadOnlyList<QuizResponse>>> Handle(ListQuizzesQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var list = await quizzes.ListByCohortAsync(request.CohortId, cancellationToken);
        IReadOnlyList<QuizResponse> items = [.. list.Select(q => q.ToResponse())];
        return Result<IReadOnlyList<QuizResponse>>.Success(items);
    }
}

public sealed record SaveQuizCommand(
    Guid CohortId,
    Guid? QuizId,
    string TitleAr,
    string TitleEn,
    int TimeLimitMinutes,
    int MaxAttempts,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int Weight,
    IReadOnlyList<QuestionDraft> Questions,
    int? DrawCount = null,
    bool Shuffle = false,
    string? ExamKey = null) : ICommand<QuizResponse>;

internal sealed class SaveQuizValidator : AbstractValidator<SaveQuizCommand>
{
    public SaveQuizValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Quiz.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Quiz.TitleMaxLength);
        RuleFor(c => c.Questions).NotNull();
    }
}

internal sealed class SaveQuizHandler(AssessmentAccess access, IQuizRepository quizzes, IAssessmentUnitOfWork unitOfWork)
    : IRequestHandler<SaveQuizCommand, QuizResponse>
{
    public async Task<Result<QuizResponse>> Handle(SaveQuizCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Quiz.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        Quiz quiz;

        if (request.QuizId is { } id)
        {
            var existing = await quizzes.FindAsync(new QuizId(id), cancellationToken);
            if (existing is null || existing.CohortId != request.CohortId)
            {
                return AssessmentErrors.QuizNotFound;
            }

            var updated = existing.Update(title.Value, request.TimeLimitMinutes, request.MaxAttempts, request.OpensAt, request.ClosesAt, request.Weight, request.Questions, request.DrawCount, request.Shuffle);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            quiz = existing;
        }
        else
        {
            var created = Quiz.Create(request.CohortId, title.Value, request.TimeLimitMinutes, request.MaxAttempts, request.OpensAt, request.ClosesAt, request.Weight, request.Questions, request.DrawCount, request.Shuffle);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            quiz = created.Value;
            quizzes.Add(quiz);
        }

        var locked = quiz.RequireExamBrowser(request.ExamKey);
        if (!locked.IsSuccess)
        {
            return locked.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return quiz.ToResponse();
    }
}

public sealed record PublishQuizCommand(Guid CohortId, Guid QuizId) : ICommand<Unit>;

internal sealed class PublishQuizHandler(AssessmentAccess access, IQuizRepository quizzes, GradebookAccess gradebooks, IAssessmentUnitOfWork unitOfWork)
    : IRequestHandler<PublishQuizCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PublishQuizCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var quiz = await quizzes.FindAsync(new QuizId(request.QuizId), cancellationToken);
        if (quiz is null || quiz.CohortId != request.CohortId)
        {
            return AssessmentErrors.QuizNotFound;
        }

        var published = quiz.Publish();
        if (!published.IsSuccess)
        {
            return published.Error!;
        }

        var gradebook = await gradebooks.OpenAsync(request.CohortId, cancellationToken);
        gradebook.AddItem(quiz.Id.Value, GradeItemKind.Quiz, quiz.Title, quiz.Weight);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record AttemptQuestion(Guid Id, QuestionKind Kind, TextDto Text, int Points, IReadOnlyList<AttemptOption> Options, IReadOnlyList<AttemptChoice> Choices, string? Response);

public sealed record AttemptOption(Guid Id, TextDto Text);

/// <summary>A right-hand item in a matching question, labelled so it cannot be traced to its pair.</summary>
public sealed record AttemptChoice(string Key, TextDto Text);

/// <summary>Never carries the correct answers.</summary>
public sealed record AttemptResponse(
    Guid Id,
    Guid QuizId,
    TextDto Title,
    int Number,
    int MaxAttempts,
    DateTimeOffset DeadlineAt,
    int SecondsLeft,
    IReadOnlyList<AttemptQuestion> Questions);

internal static class AttemptMapping
{
    public static AttemptResponse ToResponse(this QuizAttempt attempt, Quiz quiz, DateTimeOffset now)
    {
        var saved = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.Response);

        // Typed answers stay out of the page, or the accepted spellings would be readable in it.
        IReadOnlyList<AttemptOption> Options(QuizQuestion q)
        {
            if (q.Kind is QuestionKind.ShortAnswer or QuestionKind.Numeric or QuestionKind.Essay)
            {
                return [];
            }

            var options = q.Options.OrderBy(o => o.Position);
            var mix = quiz.Shuffle && q.Kind != QuestionKind.TrueFalse;

            return [.. (mix ? options.OrderBy(o => AnswerText.SecretKey(attempt.Seed, o.Id.Value), StringComparer.Ordinal) : options)
                .Select(o => new AttemptOption(o.Id.Value, new TextDto(o.Text.Ar, o.Text.En)))];
        }

        IReadOnlyList<AttemptChoice> Choices(QuizQuestion q) => q.Kind != QuestionKind.Matching
            ? []
            : [.. q.Options
                .Select(o => new AttemptChoice(AnswerText.SecretKey(attempt.Seed, o.Id.Value), new TextDto(o.MatchAr!, o.MatchEn!)))
                .OrderBy(c => c.Key, StringComparer.Ordinal)];

        return new AttemptResponse(
            attempt.Id.Value,
            quiz.Id.Value,
            new TextDto(quiz.Title.Ar, quiz.Title.En),
            attempt.Number,
            quiz.MaxAttempts,
            attempt.DeadlineAt,
            Math.Max(0, (int)(attempt.DeadlineAt - now).TotalSeconds),
            [.. attempt.QuestionsFor(quiz).Select(q => new AttemptQuestion(
                q.Id.Value,
                q.Kind,
                new TextDto(q.Text.Ar, q.Text.En),
                q.Points,
                Options(q),
                Choices(q),
                saved.GetValueOrDefault(q.Id)))]);
    }
}

/// <summary>Scores an attempt and carries the result to the gradebook.</summary>
public sealed class AttemptCloser(GradebookAccess gradebooks, TimeProvider clock)
{
    public async Task CloseAsync(QuizAttempt attempt, Quiz quiz, CancellationToken cancellationToken)
    {
        if (attempt.IsFinished)
        {
            return;
        }

        attempt.Finish(quiz, clock.GetUtcNow());

        var gradebook = await gradebooks.OpenAsync(quiz.CohortId, cancellationToken);
        var item = gradebook.ItemFor(quiz.Id.Value);

        // Grades already released stay as released; only a correction may change them.
        if (item is { IsPublished: false } && !attempt.AwaitingMarking)
        {
            gradebook.RecordQuizScore(item.Id, attempt.TraineeId, attempt.ScorePercent!.Value);
        }
    }
}

public sealed record StartAttemptCommand(Guid CohortId, Guid QuizId) : ICommand<AttemptResponse>;

internal sealed class StartAttemptHandler(
    AssessmentAccess access,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    IGradebookRepository gradebooks,
    AttemptCloser closer,
    ExamGate exam,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StartAttemptCommand, AttemptResponse>
{
    public async Task<Result<AttemptResponse>> Handle(StartAttemptCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var quiz = await quizzes.FindAsync(new QuizId(request.QuizId), cancellationToken);
        if (quiz is null || quiz.CohortId != request.CohortId || quiz.Status != QuizStatus.Published)
        {
            return AssessmentErrors.QuizNotFound;
        }

        if (!exam.Allows(quiz))
        {
            return SafeExam.Required;
        }

        var now = clock.GetUtcNow();
        var traineeId = user.Id!.Value;
        var mine = await attempts.ListAsync(quiz.Id, traineeId, cancellationToken);
        var open = mine.FirstOrDefault(a => !a.IsFinished);

        if (open is not null && !open.HasExpired(now))
        {
            return open.ToResponse(quiz, now);
        }

        if (open is not null)
        {
            await closer.CloseAsync(open, quiz, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);

        if (!quiz.IsOpen(now) || gradebook?.ItemFor(quiz.Id.Value) is { IsPublished: true })
        {
            return AssessmentErrors.QuizClosed;
        }

        if (mine.Count >= quiz.MaxAttempts)
        {
            return AssessmentErrors.NoAttemptsLeft;
        }

        var attempt = QuizAttempt.Start(quiz, traineeId, mine.Count + 1, now);
        attempts.Add(attempt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return attempt.ToResponse(quiz, now);
    }
}

public sealed record SaveAnswerCommand(Guid CohortId, Guid AttemptId, Guid QuestionId, string? Response) : ICommand<Unit>, INotAudited;

internal sealed class SaveAnswerValidator : AbstractValidator<SaveAnswerCommand>
{
    public SaveAnswerValidator() => RuleFor(c => c.Response).MaximumLength(QuizQuestion.MaxEssayLength);
}

internal sealed class SaveAnswerHandler(
    AssessmentAccess access,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    ExamGate exam,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SaveAnswerCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SaveAnswerCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var attempt = await attempts.FindAsync(new AttemptId(request.AttemptId), cancellationToken);
        var quiz = attempt is null ? null : await quizzes.FindAsync(attempt.QuizId, cancellationToken);

        if (attempt is null || quiz is null || attempt.TraineeId != user.Id || quiz.CohortId != request.CohortId)
        {
            return AssessmentErrors.AttemptNotFound;
        }

        if (!exam.Allows(quiz))
        {
            return SafeExam.Required;
        }

        var result = attempt.SaveAnswer(quiz, new QuestionId(request.QuestionId), request.Response, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record SubmitAttemptCommand(Guid CohortId, Guid AttemptId) : ICommand<Unit>;

internal sealed class SubmitAttemptHandler(
    AssessmentAccess access,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    AttemptCloser closer,
    ExamGate exam,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork) : IRequestHandler<SubmitAttemptCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SubmitAttemptCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var attempt = await attempts.FindAsync(new AttemptId(request.AttemptId), cancellationToken);
        var quiz = attempt is null ? null : await quizzes.FindAsync(attempt.QuizId, cancellationToken);

        if (attempt is null || quiz is null || attempt.TraineeId != user.Id || quiz.CohortId != request.CohortId)
        {
            return AssessmentErrors.AttemptNotFound;
        }

        if (!exam.Allows(quiz))
        {
            return SafeExam.Required;
        }

        await closer.CloseAsync(attempt, quiz, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
