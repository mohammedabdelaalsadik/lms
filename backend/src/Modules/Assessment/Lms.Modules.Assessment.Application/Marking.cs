using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public sealed record EssayToMark(Guid AttemptId, Guid QuestionId, TextDto Trainee, TextDto Question, string Answer, int MaxPoints, decimal? Awarded);

public sealed record ListEssaysQuery(Guid CohortId, Guid QuizId) : IQuery<IReadOnlyList<EssayToMark>>;

internal sealed class ListEssaysHandler(AssessmentAccess access, IQuizRepository quizzes, IAttemptRepository attempts, IUserDirectory directory)
    : IRequestHandler<ListEssaysQuery, IReadOnlyList<EssayToMark>>
{
    public async Task<Result<IReadOnlyList<EssayToMark>>> Handle(ListEssaysQuery request, CancellationToken cancellationToken)
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

        var waiting = await attempts.ListAwaitingMarkingAsync(quiz.Id, cancellationToken);
        var people = await directory.GetAsync([.. waiting.Select(a => a.TraineeId).Distinct()], cancellationToken);

        IReadOnlyList<EssayToMark> items =
        [
            .. from attempt in waiting
               from question in attempt.QuestionsFor(quiz)
               where question.Kind == QuestionKind.Essay
               let answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == question.Id)
               where answer is not null
               let person = people.GetValueOrDefault(attempt.TraineeId)
               select new EssayToMark(
                   attempt.Id.Value,
                   question.Id.Value,
                   new TextDto(person?.NameAr ?? "", person?.NameEn ?? ""),
                   new TextDto(question.Text.Ar, question.Text.En),
                   answer.Response,
                   question.Points,
                   answer.AwardedPoints),
        ];

        return Result<IReadOnlyList<EssayToMark>>.Success(items);
    }
}

public sealed record MarkEssayCommand(Guid CohortId, Guid AttemptId, Guid QuestionId, decimal Points) : ICommand<Unit>;

internal sealed class MarkEssayValidator : AbstractValidator<MarkEssayCommand>
{
    public MarkEssayValidator() => RuleFor(c => c.Points).GreaterThanOrEqualTo(0);
}

internal sealed class MarkEssayHandler(
    AssessmentAccess access,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    GradebookAccess gradebooks,
    IAssessmentUnitOfWork unitOfWork) : IRequestHandler<MarkEssayCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MarkEssayCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var attempt = await attempts.FindAsync(new AttemptId(request.AttemptId), cancellationToken);
        var quiz = attempt is null ? null : await quizzes.FindAsync(attempt.QuizId, cancellationToken);

        if (attempt is null || quiz is null || quiz.CohortId != request.CohortId)
        {
            return AssessmentErrors.AttemptNotFound;
        }

        var marked = attempt.MarkEssay(quiz, new QuestionId(request.QuestionId), request.Points);
        if (!marked.IsSuccess)
        {
            return marked.Error!;
        }

        if (!attempt.AwaitingMarking)
        {
            var gradebook = await gradebooks.OpenAsync(quiz.CohortId, cancellationToken);

            if (gradebook.ItemFor(quiz.Id.Value) is { IsPublished: false } item)
            {
                gradebook.RecordQuizScore(item.Id, attempt.TraineeId, attempt.ScorePercent!.Value);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
