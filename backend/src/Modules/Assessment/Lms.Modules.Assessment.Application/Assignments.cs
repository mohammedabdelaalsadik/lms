using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public sealed record AssignmentResponse(Guid Id, TextDto Title, TextDto Instructions, DateTimeOffset DueAt, int Weight);

public sealed record CreateAssignmentCommand(
    Guid CohortId,
    string TitleAr,
    string TitleEn,
    string InstructionsAr,
    string InstructionsEn,
    DateTimeOffset DueAt,
    int Weight) : ICommand<AssignmentResponse>;

internal sealed class CreateAssignmentValidator : AbstractValidator<CreateAssignmentCommand>
{
    public CreateAssignmentValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Assignment.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Assignment.TitleMaxLength);
        RuleFor(c => c.InstructionsAr).NotEmpty().MaximumLength(Assignment.InstructionsMaxLength);
        RuleFor(c => c.InstructionsEn).NotEmpty().MaximumLength(Assignment.InstructionsMaxLength);
    }
}

internal static class AssignmentMapping
{
    public static AssignmentResponse ToResponse(this Assignment a) =>
        new(a.Id.Value, new TextDto(a.Title.Ar, a.Title.En), new TextDto(a.Instructions.Ar, a.Instructions.En), a.DueAt, a.Weight);
}

internal sealed class CreateAssignmentHandler(AssessmentAccess access, IAssignmentRepository assignments, GradebookAccess gradebooks, IAssessmentUnitOfWork unitOfWork)
    : IRequestHandler<CreateAssignmentCommand, AssignmentResponse>
{
    public async Task<Result<AssignmentResponse>> Handle(CreateAssignmentCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Assignment.TitleMaxLength);
        var instructions = LocalizedText.Create(request.InstructionsAr, request.InstructionsEn, Assignment.InstructionsMaxLength);
        if (!title.IsSuccess || !instructions.IsSuccess)
        {
            return (title.Error ?? instructions.Error)!;
        }

        var assignment = Assignment.Create(request.CohortId, title.Value, instructions.Value, request.DueAt, request.Weight);
        if (!assignment.IsSuccess)
        {
            return assignment.Error!;
        }

        assignments.Add(assignment.Value);
        var gradebook = await gradebooks.OpenAsync(request.CohortId, cancellationToken);
        gradebook.AddItem(assignment.Value.Id.Value, GradeItemKind.Assignment, title.Value, request.Weight);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment.Value.ToResponse();
    }
}

public sealed record ListAssignmentsQuery(Guid CohortId) : IQuery<IReadOnlyList<AssignmentResponse>>;

internal sealed class ListAssignmentsHandler(AssessmentAccess access, IAssignmentRepository assignments) : IRequestHandler<ListAssignmentsQuery, IReadOnlyList<AssignmentResponse>>
{
    public async Task<Result<IReadOnlyList<AssignmentResponse>>> Handle(ListAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var list = await assignments.ListByCohortAsync(request.CohortId, cancellationToken);
        IReadOnlyList<AssignmentResponse> items = [.. list.Select(a => a.ToResponse())];
        return Result<IReadOnlyList<AssignmentResponse>>.Success(items);
    }
}

public sealed record SubmitAssignmentCommand(Guid CohortId, Guid AssignmentId, string FileName, long Size, Stream Content) : ICommand<Unit>;

internal sealed class SubmitAssignmentHandler(
    AssessmentAccess access,
    IAssignmentRepository assignments,
    IGradebookRepository gradebooks,
    IFileStorage storage,
    ICurrentUser user,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SubmitAssignmentCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SubmitAssignmentCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var assignment = await assignments.FindAsync(new AssignmentId(request.AssignmentId), cancellationToken);
        if (assignment is null || assignment.CohortId != request.CohortId)
        {
            return AssessmentErrors.AssignmentNotFound;
        }

        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        if (gradebook?.ItemFor(assignment.Id.Value) is { IsPublished: true })
        {
            return AssessmentErrors.SubmissionClosed;
        }

        var fileName = Path.GetFileName(request.FileName);
        var contentType = FileRules.ContentTypeFor(fileName);

        if (contentType is null || fileName.Length > 255)
        {
            return FileErrors.TypeNotAllowed;
        }

        if (request.Size is <= 0 or > FileRules.MaxSize)
        {
            return FileErrors.TooLarge;
        }

        var traineeId = user.Id!.Value;
        var existing = await assignments.FindSubmissionAsync(assignment.Id, traineeId, cancellationToken);

        if (existing?.ReviewedAt is not null)
        {
            return AssessmentErrors.SubmissionClosed;
        }

        var key = await storage.SaveAsync(request.Content, cancellationToken);
        var file = new SubmittedFile(key, fileName, contentType, request.Size);
        var now = clock.GetUtcNow();
        string? replaced = null;

        if (existing is null)
        {
            assignments.Add(AssignmentSubmission.Submit(assignment, traineeId, file, now));
        }
        else
        {
            replaced = existing.Replace(assignment, file, now).Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (replaced is not null)
        {
            storage.Delete(replaced);
        }

        return default(Unit);
    }
}

public sealed record SubmissionEntry(
    Guid Id,
    Guid TraineeId,
    TextDto TraineeName,
    string MaskedNationalId,
    string FileName,
    long FileSize,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    string? Feedback,
    DateTimeOffset? ReviewedAt,
    decimal? Percent);

public sealed record SubmissionsResponse(AssignmentResponse Assignment, bool GradesPublished, IReadOnlyList<SubmissionEntry> Entries);

public sealed record ListSubmissionsQuery(Guid CohortId, Guid AssignmentId) : IQuery<SubmissionsResponse>;

internal sealed class ListSubmissionsHandler(
    AssessmentAccess access,
    IAssignmentRepository assignments,
    IGradebookRepository gradebooks,
    IUserDirectory directory) : IRequestHandler<ListSubmissionsQuery, SubmissionsResponse>
{
    public async Task<Result<SubmissionsResponse>> Handle(ListSubmissionsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var assignment = await assignments.FindAsync(new AssignmentId(request.AssignmentId), cancellationToken);
        if (assignment is null || assignment.CohortId != request.CohortId)
        {
            return AssessmentErrors.AssignmentNotFound;
        }

        var submissions = await assignments.ListSubmissionsAsync(assignment.Id, cancellationToken);
        var names = await directory.GetAsync([.. submissions.Select(s => s.TraineeId)], cancellationToken);
        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);
        var item = gradebook?.ItemFor(assignment.Id.Value);

        return new SubmissionsResponse(
            assignment.ToResponse(),
            item?.IsPublished ?? false,
            [.. submissions.Select(s =>
            {
                var name = names.GetValueOrDefault(s.TraineeId);
                return new SubmissionEntry(
                    s.Id.Value,
                    s.TraineeId,
                    new TextDto(name?.NameAr ?? "", name?.NameEn ?? ""),
                    name?.MaskedNationalId ?? "",
                    s.FileName,
                    s.FileSize,
                    s.SubmittedAt,
                    s.IsLate,
                    s.Feedback,
                    s.ReviewedAt,
                    item is null ? null : gradebook!.GradeOf(item.Id, s.TraineeId));
            })]);
    }
}

public sealed record ReviewSubmissionCommand(Guid CohortId, Guid SubmissionId, decimal Percent, string? Feedback) : ICommand<Unit>;

internal sealed class ReviewSubmissionValidator : AbstractValidator<ReviewSubmissionCommand>
{
    public ReviewSubmissionValidator() => RuleFor(c => c.Feedback).MaximumLength(AssignmentSubmission.FeedbackMaxLength);
}

internal sealed class ReviewSubmissionHandler(
    AssessmentAccess access,
    IAssignmentRepository assignments,
    GradebookAccess gradebooks,
    FinalGradePublisher finals,
    IAssessmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ReviewSubmissionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ReviewSubmissionCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var submission = await assignments.FindSubmissionAsync(new SubmissionId(request.SubmissionId), cancellationToken);
        var assignment = submission is null ? null : await assignments.FindAsync(submission.AssignmentId, cancellationToken);

        if (submission is null || assignment is null || assignment.CohortId != request.CohortId)
        {
            return AssessmentErrors.SubmissionNotFound;
        }

        var gradebook = await gradebooks.OpenAsync(request.CohortId, cancellationToken);
        var item = gradebook.ItemFor(assignment.Id.Value);

        if (item is null)
        {
            return AssessmentErrors.ItemNotFound;
        }

        var graded = gradebook.SetGrade(item.Id, submission.TraineeId, request.Percent);
        if (!graded.IsSuccess)
        {
            return graded.Error!;
        }

        submission.Review(request.Feedback, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await finals.PublishAsync(cohort.Value, gradebook, cancellationToken);
        return default(Unit);
    }
}

public sealed record DownloadSubmissionQuery(Guid CohortId, Guid SubmissionId) : IQuery<SubmissionFile>, IAuditedRequest;

public sealed record SubmissionFile(Stream Content, string FileName, string ContentType);

internal sealed class DownloadSubmissionHandler(
    AssessmentAccess access,
    IAssignmentRepository assignments,
    IFileStorage storage) : IRequestHandler<DownloadSubmissionQuery, SubmissionFile>
{
    public async Task<Result<SubmissionFile>> Handle(DownloadSubmissionQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var submission = await assignments.FindSubmissionAsync(new SubmissionId(request.SubmissionId), cancellationToken);
        var assignment = submission is null ? null : await assignments.FindAsync(submission.AssignmentId, cancellationToken);

        if (submission is null || assignment is null || assignment.CohortId != request.CohortId)
        {
            return AssessmentErrors.SubmissionNotFound;
        }

        return new SubmissionFile(storage.Open(submission.FileKey), submission.FileName, submission.ContentType);
    }
}
