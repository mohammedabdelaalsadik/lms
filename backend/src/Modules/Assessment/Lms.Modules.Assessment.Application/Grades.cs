using FluentValidation;
using System.Globalization;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public sealed record GradebookItemDto(Guid Id, Guid SourceId, GradeItemKind Kind, TextDto Title, int Weight, bool IsPublished);

public sealed record GradeCell(Guid ItemId, decimal? Percent);

public sealed record GradebookRow(Guid TraineeId, TextDto Name, string MaskedNationalId, IReadOnlyList<GradeCell> Cells, decimal? FinalGrade);

public sealed record CorrectionDto(
    Guid Id,
    Guid ItemId,
    TextDto ItemTitle,
    Guid TraineeId,
    TextDto TraineeName,
    decimal OldPercent,
    decimal NewPercent,
    string Reason,
    CorrectionStatus Status,
    DateTimeOffset RequestedAt);

public sealed record GradebookResponse(
    IReadOnlyList<GradebookItemDto> Items,
    IReadOnlyList<GradebookRow> Rows,
    IReadOnlyList<CorrectionDto> Corrections);

public sealed record GetGradebookQuery(Guid CohortId) : IQuery<GradebookResponse>;

internal sealed class GetGradebookHandler(
    AssessmentAccess access,
    IGradebookRepository gradebooks,
    ICohortDirectory cohorts,
    IUserDirectory directory) : IRequestHandler<GetGradebookQuery, GradebookResponse>
{
    public async Task<Result<GradebookResponse>> Handle(GetGradebookQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        var trainees = await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken);
        var names = await directory.GetAsync(trainees, cancellationToken);

        if (gradebook is null)
        {
            return new GradebookResponse([], [], []);
        }

        // Ids are time-ordered, so this keeps columns in the order items were created.
        var items = gradebook.Items.OrderBy(i => i.Id.Value).ToList();
        var titles = items.ToDictionary(i => i.Id, i => new TextDto(i.Title.Ar, i.Title.En));
        TextDto Name(Guid id) => new(names.GetValueOrDefault(id)?.NameAr ?? "", names.GetValueOrDefault(id)?.NameEn ?? "");

        return new GradebookResponse(
            [.. items.Select(i => new GradebookItemDto(i.Id.Value, i.SourceId, i.Kind, titles[i.Id], i.Weight, i.IsPublished))],
            [.. trainees.Select(t => new GradebookRow(
                t,
                Name(t),
                names.GetValueOrDefault(t)?.MaskedNationalId ?? "",
                [.. items.Select(i => new GradeCell(i.Id.Value, gradebook.GradeOf(i.Id, t)))],
                gradebook.FinalGrade(t)))],
            [.. gradebook.Corrections.OrderByDescending(c => c.RequestedAt).Select(c => new CorrectionDto(
                c.Id.Value,
                c.ItemId.Value,
                titles.GetValueOrDefault(c.ItemId, new TextDto("", "")),
                c.TraineeId,
                Name(c.TraineeId),
                c.OldPercent,
                c.NewPercent,
                c.Reason,
                c.Status,
                c.RequestedAt))]);
    }
}

public sealed record ExportGradebookQuery(Guid CohortId) : IQuery<CsvFile>, IAuditedRequest;

internal sealed class ExportGradebookHandler(IRequestHandler<GetGradebookQuery, GradebookResponse> gradebook) : IRequestHandler<ExportGradebookQuery, CsvFile>
{
    public async Task<Result<CsvFile>> Handle(ExportGradebookQuery request, CancellationToken cancellationToken)
    {
        var book = await gradebook.Handle(new GetGradebookQuery(request.CohortId), cancellationToken);
        if (!book.IsSuccess)
        {
            return book.Error!;
        }

        IReadOnlyList<string?> header = ["الاسم / Name", "Name", "الهوية / ID", .. book.Value.Items.Select(i => $"{i.Title.Ar} / {i.Title.En} ({i.Weight}%)"), "النهائي / Final"];

        var rows = book.Value.Rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Name.Ar,
            r.Name.En,
            r.MaskedNationalId,
            .. book.Value.Items.Select(i => r.Cells.FirstOrDefault(c => c.ItemId == i.Id)?.Percent?.ToString(CultureInfo.InvariantCulture)),
            r.FinalGrade?.ToString(CultureInfo.InvariantCulture),
        ]);

        return CsvWriter.Write($"gradebook-{request.CohortId:N}.csv", new[] { header }.Concat(rows));
    }
}

public sealed record SetGradeCommand(Guid CohortId, Guid ItemId, Guid TraineeId, decimal Percent) : ICommand<Unit>;

internal sealed class SetGradeHandler(
    AssessmentAccess access,
    IGradebookRepository gradebooks,
    ICohortDirectory cohorts,
    FinalGradePublisher finals,
    IAssessmentUnitOfWork unitOfWork) : IRequestHandler<SetGradeCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SetGradeCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        if (gradebook is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        if (!await cohorts.IsEnrolledAsync(request.CohortId, request.TraineeId, cancellationToken))
        {
            return AssessmentErrors.NotEnrolled;
        }

        var result = gradebook.SetGrade(new GradeItemId(request.ItemId), request.TraineeId, request.Percent);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await finals.PublishAsync(cohort.Value, gradebook, cancellationToken);
        return default(Unit);
    }
}

public sealed record ChangeWeightCommand(Guid CohortId, Guid ItemId, int Weight) : ICommand<Unit>;

internal sealed class ChangeWeightHandler(AssessmentAccess access, IGradebookRepository gradebooks, IAssessmentUnitOfWork unitOfWork)
    : IRequestHandler<ChangeWeightCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ChangeWeightCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        if (gradebook is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        var result = gradebook.ChangeWeight(new GradeItemId(request.ItemId), request.Weight);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record PublishGradesCommand(Guid CohortId, Guid ItemId) : ICommand<Unit>;

internal sealed class PublishGradesHandler(
    AssessmentAccess access,
    IGradebookRepository gradebooks,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    AttemptCloser closer,
    FinalGradePublisher finals,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<PublishGradesCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PublishGradesCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        var item = gradebook?.Items.FirstOrDefault(i => i.Id == new GradeItemId(request.ItemId));

        if (gradebook is null || item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        if (item.Kind == GradeItemKind.Quiz)
        {
            await CloseExpiredAttemptsAsync(item.SourceId, cancellationToken);

            if ((await attempts.ListAwaitingMarkingAsync(new QuizId(item.SourceId), cancellationToken)).Count > 0)
            {
                return AssessmentErrors.MarkingPending;
            }
        }

        var result = gradebook.Publish(item.Id, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await finals.PublishAsync(cohort.Value, gradebook, cancellationToken);
        return default(Unit);
    }

    /// <summary>A trainee who ran out of time and never returned still gets the answers they saved.</summary>
    private async Task CloseExpiredAttemptsAsync(Guid quizSourceId, CancellationToken cancellationToken)
    {
        var quizId = new QuizId(quizSourceId);
        var quiz = await quizzes.FindAsync(quizId, cancellationToken);
        if (quiz is null)
        {
            return;
        }

        foreach (var attempt in await attempts.ListOpenByQuizAsync(quizId, cancellationToken))
        {
            await closer.CloseAsync(attempt, quiz, cancellationToken);
        }
    }
}

public sealed record RequestCorrectionCommand(Guid CohortId, Guid ItemId, Guid TraineeId, decimal NewPercent, string Reason) : ICommand<Unit>;

internal sealed class RequestCorrectionValidator : AbstractValidator<RequestCorrectionCommand>
{
    public RequestCorrectionValidator() => RuleFor(c => c.Reason).MaximumLength(GradeCorrection.ReasonMaxLength);
}

internal sealed class RequestCorrectionHandler(
    AssessmentAccess access,
    IGradebookRepository gradebooks,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RequestCorrectionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RequestCorrectionCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        if (gradebook is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        var result = gradebook.RequestCorrection(new GradeItemId(request.ItemId), request.TraineeId, request.NewPercent, request.Reason, user.Id!.Value, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record DecideCorrectionCommand(Guid CohortId, Guid CorrectionId, bool Approve) : ICommand<Unit>;

internal sealed class DecideCorrectionHandler(
    AssessmentAccess access,
    IGradebookRepository gradebooks,
    FinalGradePublisher finals,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<DecideCorrectionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DecideCorrectionCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        if (gradebook is null)
        {
            return AssessmentErrors.CorrectionNotFound;
        }

        var id = new CorrectionId(request.CorrectionId);
        var now = clock.GetUtcNow();
        var outcome = request.Approve
            ? gradebook.ApproveCorrection(id, user.Id!.Value, now).Error
            : gradebook.RejectCorrection(id, user.Id!.Value, now).Error;

        if (outcome is not null)
        {
            return outcome;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Approve)
        {
            await finals.PublishAsync(cohort.Value, gradebook, cancellationToken);
        }

        return default(Unit);
    }
}

public sealed record MyQuiz(Guid Id, TextDto Title, int TimeLimitMinutes, int MaxAttempts, int AttemptsUsed, bool IsOpen, bool HasOpenAttempt, DateTimeOffset? ClosesAt);

public sealed record MyAssignment(
    Guid Id,
    TextDto Title,
    TextDto Instructions,
    DateTimeOffset DueAt,
    string? FileName,
    DateTimeOffset? SubmittedAt,
    bool IsLate,
    bool CanSubmit,
    string? Feedback);

public sealed record MyGrade(Guid ItemId, TextDto Title, int Weight, decimal? Percent);

public sealed record MyAssessmentsResponse(
    IReadOnlyList<MyQuiz> Quizzes,
    IReadOnlyList<MyAssignment> Assignments,
    IReadOnlyList<MyGrade> Grades,
    decimal? RunningTotal,
    decimal? FinalGrade);

public sealed record GetMyAssessmentsQuery(Guid CohortId) : IQuery<MyAssessmentsResponse>;

internal sealed class GetMyAssessmentsHandler(
    AssessmentAccess access,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    IAssignmentRepository assignments,
    IGradebookRepository gradebooks,
    ICurrentUser user,
    TimeProvider clock) : IRequestHandler<GetMyAssessmentsQuery, MyAssessmentsResponse>
{
    public async Task<Result<MyAssessmentsResponse>> Handle(GetMyAssessmentsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var traineeId = user.Id!.Value;
        var now = clock.GetUtcNow();
        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);

        bool Released(Guid sourceId) => gradebook?.ItemFor(sourceId) is { IsPublished: true };

        var published = (await quizzes.ListByCohortAsync(request.CohortId, cancellationToken)).Where(q => q.Status == QuizStatus.Published).ToList();
        var mine = await attempts.ListForTraineeAsync([.. published.Select(q => q.Id)], traineeId, cancellationToken);

        var assignmentList = await assignments.ListByCohortAsync(request.CohortId, cancellationToken);
        var submissions = (await assignments.ListSubmissionsForTraineeAsync([.. assignmentList.Select(a => a.Id)], traineeId, cancellationToken))
            .ToDictionary(s => s.AssignmentId);

        var gradeRows = gradebook?.Items.Where(i => i.IsPublished).OrderBy(i => i.Id.Value).Select(i => new MyGrade(
            i.Id.Value,
            new TextDto(i.Title.Ar, i.Title.En),
            i.Weight,
            gradebook.GradeOf(i.Id, traineeId))).ToList() ?? [];

        return new MyAssessmentsResponse(
            [.. published.Select(q =>
            {
                var used = mine.Where(a => a.QuizId == q.Id).ToList();
                var hasOpen = used.Exists(a => !a.IsFinished && !a.HasExpired(now));
                return new MyQuiz(
                    q.Id.Value,
                    new TextDto(q.Title.Ar, q.Title.En),
                    q.TimeLimitMinutes,
                    q.MaxAttempts,
                    used.Count,
                    q.IsOpen(now) && !Released(q.Id.Value),
                    hasOpen,
                    q.ClosesAt);
            })],
            [.. assignmentList.Select(a =>
            {
                var submission = submissions.GetValueOrDefault(a.Id);
                var released = Released(a.Id.Value);
                return new MyAssignment(
                    a.Id.Value,
                    new TextDto(a.Title.Ar, a.Title.En),
                    new TextDto(a.Instructions.Ar, a.Instructions.En),
                    a.DueAt,
                    submission?.FileName,
                    submission?.SubmittedAt,
                    submission?.IsLate ?? false,
                    !released && submission?.ReviewedAt is null,
                    released ? submission?.Feedback : null);
            })],
            gradeRows,
            gradebook?.RunningTotal(traineeId),
            gradebook?.FinalGrade(traineeId));
    }
}
