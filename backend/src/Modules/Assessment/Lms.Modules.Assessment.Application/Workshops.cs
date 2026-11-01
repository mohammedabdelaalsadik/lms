using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public interface IWorkshopRepository
{
    Task<Workshop?> FindAsync(WorkshopId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Workshop>> ListAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkshopSubmission>> ListSubmissionsAsync(IReadOnlyCollection<WorkshopId> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkshopReview>> ListReviewsAsync(IReadOnlyCollection<WorkshopId> ids, CancellationToken cancellationToken);

    Task<WorkshopSubmission?> FindSubmissionAsync(WorkshopId id, Guid traineeId, CancellationToken cancellationToken);

    Task<WorkshopReview?> FindReviewAsync(Guid id, CancellationToken cancellationToken);

    void Add(Workshop workshop);

    void Add(WorkshopSubmission submission);

    void Add(WorkshopReview review);
}

public sealed record WorkshopEntry(TextDto? Trainee, string Text, DateTimeOffset SubmittedAt, int ReviewsDone, int ReviewsAssigned, decimal? Average);

public sealed record StaffWorkshop(Guid Id, TextDto Title, TextDto Instructions, WorkshopStatus Status, int ReviewsPerPerson, int Weight, IReadOnlyList<WorkshopEntry> Entries);

public sealed record ListWorkshopsQuery(Guid CohortId) : IQuery<IReadOnlyList<StaffWorkshop>>;

internal sealed class ListWorkshopsHandler(AssessmentAccess access, IWorkshopRepository workshops, IUserDirectory directory) : IRequestHandler<ListWorkshopsQuery, IReadOnlyList<StaffWorkshop>>
{
    public async Task<Result<IReadOnlyList<StaffWorkshop>>> Handle(ListWorkshopsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var list = await workshops.ListAsync(request.CohortId, cancellationToken);
        List<WorkshopId> ids = [.. list.Select(w => w.Id)];
        var submissions = await workshops.ListSubmissionsAsync(ids, cancellationToken);
        var reviews = await workshops.ListReviewsAsync(ids, cancellationToken);
        var people = await directory.GetAsync([.. submissions.Select(s => s.TraineeId).Distinct()], cancellationToken);

        IReadOnlyList<StaffWorkshop> items =
        [
            .. list.Select(w => new StaffWorkshop(
                w.Id.Value,
                new TextDto(w.Title.Ar, w.Title.En),
                new TextDto(w.Instructions.Ar, w.Instructions.En),
                w.Status,
                w.ReviewsPerPerson,
                w.Weight,
                [
                    .. submissions.Where(s => s.WorkshopId == w.Id).Select(s =>
                    {
                        var mine = reviews.Where(r => r.SubmissionId == s.Id).ToList();
                        var person = people.GetValueOrDefault(s.TraineeId);
                        return new WorkshopEntry(
                            person is null ? null : new TextDto(person.NameAr, person.NameEn),
                            s.Text,
                            s.SubmittedAt,
                            mine.Count(r => r.Score is not null),
                            mine.Count,
                            Workshop.Average(mine.Select(r => r.Score)));
                    }),
                ])),
        ];

        return Result<IReadOnlyList<StaffWorkshop>>.Success(items);
    }
}

public sealed record CreateWorkshopCommand(Guid CohortId, string TitleAr, string TitleEn, string InstructionsAr, string InstructionsEn, int ReviewsPerPerson, int Weight) : ICommand<Guid>;

internal sealed class CreateWorkshopValidator : AbstractValidator<CreateWorkshopCommand>
{
    public CreateWorkshopValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Workshop.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Workshop.TitleMaxLength);
        RuleFor(c => c.InstructionsAr).NotEmpty().MaximumLength(Workshop.InstructionsMaxLength);
        RuleFor(c => c.InstructionsEn).NotEmpty().MaximumLength(Workshop.InstructionsMaxLength);
    }
}

internal sealed class CreateWorkshopHandler(AssessmentAccess access, IWorkshopRepository workshops, GradebookAccess gradebooks, IAssessmentUnitOfWork unitOfWork) : IRequestHandler<CreateWorkshopCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateWorkshopCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Workshop.TitleMaxLength);
        var instructions = LocalizedText.Create(request.InstructionsAr, request.InstructionsEn, Workshop.InstructionsMaxLength);
        if (!title.IsSuccess || !instructions.IsSuccess)
        {
            return (title.Error ?? instructions.Error)!;
        }

        var workshop = Workshop.Create(request.CohortId, title.Value, instructions.Value, request.ReviewsPerPerson, request.Weight);
        if (!workshop.IsSuccess)
        {
            return workshop.Error!;
        }

        workshops.Add(workshop.Value);
        var gradebook = await gradebooks.OpenAsync(request.CohortId, cancellationToken);
        gradebook.AddItem(workshop.Value.Id.Value, GradeItemKind.Workshop, title.Value, request.Weight);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return workshop.Value.Id.Value;
    }
}

/// <summary>Moves a workshop on: from submissions to reviews, which deals out the work, or from reviews to closed, which fills in the grades.</summary>
public sealed record AdvanceWorkshopCommand(Guid CohortId, Guid WorkshopId) : ICommand<WorkshopStatus>;

internal sealed class AdvanceWorkshopHandler(AssessmentAccess access, IWorkshopRepository workshops, GradebookAccess gradebooks, IAssessmentUnitOfWork unitOfWork) : IRequestHandler<AdvanceWorkshopCommand, WorkshopStatus>
{
    public async Task<Result<WorkshopStatus>> Handle(AdvanceWorkshopCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.StaffAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var workshop = await workshops.FindAsync(new WorkshopId(request.WorkshopId), cancellationToken);
        if (workshop is null || workshop.CohortId != request.CohortId)
        {
            return WorkshopErrors.NotFound;
        }

        var submissions = await workshops.ListSubmissionsAsync([workshop.Id], cancellationToken);
        var outcome = workshop.Status == WorkshopStatus.Submission ? Start(workshop, submissions) : await CloseAsync(workshop, submissions, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return workshop.Status;
    }

    private Result Start(Workshop workshop, IReadOnlyList<WorkshopSubmission> submissions)
    {
        var started = workshop.StartReviews(submissions.Count);
        if (!started.IsSuccess)
        {
            return started;
        }

        var byAuthor = submissions.ToDictionary(s => s.TraineeId);
        foreach (var (author, reviewer) in ReviewAllocator.Deal([.. byAuthor.Keys], workshop.ReviewsPerPerson, Random.Shared))
        {
            workshops.Add(WorkshopReview.Assign(workshop.Id, byAuthor[author].Id, reviewer));
        }

        return Result.Success();
    }

    private async Task<Result> CloseAsync(Workshop workshop, IReadOnlyList<WorkshopSubmission> submissions, CancellationToken cancellationToken)
    {
        var closed = workshop.Close();
        if (!closed.IsSuccess)
        {
            return closed;
        }

        var reviews = await workshops.ListReviewsAsync([workshop.Id], cancellationToken);
        var gradebook = await gradebooks.OpenAsync(workshop.CohortId, cancellationToken);
        var item = gradebook.ItemFor(workshop.Id.Value);

        foreach (var submission in submissions)
        {
            if (item is not null && Workshop.Average(reviews.Where(r => r.SubmissionId == submission.Id).Select(r => r.Score)) is { } average)
            {
                var graded = gradebook.SetGrade(item.Id, submission.TraineeId, average);
                if (!graded.IsSuccess)
                {
                    return graded;
                }
            }
        }

        return Result.Success();
    }
}

public sealed record ReviewTask(Guid Id, string Text, decimal? Score, string? Comment);

public sealed record ReceivedReview(decimal? Score, string? Comment);

public sealed record MyWorkshop(Guid Id, TextDto Title, TextDto Instructions, WorkshopStatus Status, string? MyText, IReadOnlyList<ReviewTask> ToReview, IReadOnlyList<ReceivedReview> Received);

public sealed record ListMyWorkshopsQuery(Guid CohortId) : IQuery<IReadOnlyList<MyWorkshop>>;

internal sealed class ListMyWorkshopsHandler(AssessmentAccess access, IWorkshopRepository workshops, IGradebookRepository gradebooks, ICurrentUser user) : IRequestHandler<ListMyWorkshopsQuery, IReadOnlyList<MyWorkshop>>
{
    public async Task<Result<IReadOnlyList<MyWorkshop>>> Handle(ListMyWorkshopsQuery request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var me = user.Id!.Value;
        var list = await workshops.ListAsync(request.CohortId, cancellationToken);
        List<WorkshopId> ids = [.. list.Select(w => w.Id)];
        var submissions = await workshops.ListSubmissionsAsync(ids, cancellationToken);
        var reviews = await workshops.ListReviewsAsync(ids, cancellationToken);
        var gradebook = await gradebooks.FindAsync(request.CohortId, cancellationToken);

        IReadOnlyList<MyWorkshop> items =
        [
            .. list.Select(w =>
            {
                var mine = submissions.FirstOrDefault(s => s.WorkshopId == w.Id && s.TraineeId == me);

                // Work to review shows only while reviews are open. Scores stay hidden until the grade is published.
                IReadOnlyList<ReviewTask> toReview = w.Status != WorkshopStatus.Review ? [] :
                [
                    .. reviews.Where(r => r.WorkshopId == w.Id && r.ReviewerId == me)
                        .Select(r => new ReviewTask(r.Id, submissions.First(s => s.Id == r.SubmissionId).Text, r.Score, r.Comment)),
                ];

                var published = gradebook?.ItemFor(w.Id.Value)?.IsPublished == true;
                IReadOnlyList<ReceivedReview> received = mine is null || w.Status != WorkshopStatus.Closed ? [] :
                [
                    .. reviews.Where(r => r.SubmissionId == mine.Id && r.Score is not null)
                        .Select(r => new ReceivedReview(published ? r.Score : null, r.Comment)),
                ];

                return new MyWorkshop(w.Id.Value, new TextDto(w.Title.Ar, w.Title.En), new TextDto(w.Instructions.Ar, w.Instructions.En), w.Status, mine?.Text, toReview, received);
            }),
        ];

        return Result<IReadOnlyList<MyWorkshop>>.Success(items);
    }
}

public sealed record SubmitWorkshopCommand(Guid CohortId, Guid WorkshopId, string Text) : ICommand<Unit>;

internal sealed class SubmitWorkshopValidator : AbstractValidator<SubmitWorkshopCommand>
{
    public SubmitWorkshopValidator() => RuleFor(c => c.Text).NotEmpty().MaximumLength(Workshop.TextMaxLength);
}

internal sealed class SubmitWorkshopHandler(AssessmentAccess access, IWorkshopRepository workshops, ICurrentUser user, IAssessmentUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<SubmitWorkshopCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SubmitWorkshopCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var workshop = await workshops.FindAsync(new WorkshopId(request.WorkshopId), cancellationToken);
        if (workshop is null || workshop.CohortId != request.CohortId)
        {
            return WorkshopErrors.NotFound;
        }

        if (workshop.Status != WorkshopStatus.Submission)
        {
            return WorkshopErrors.SubmissionClosed;
        }

        var me = user.Id!.Value;
        var existing = await workshops.FindSubmissionAsync(workshop.Id, me, cancellationToken);
        if (existing is null)
        {
            workshops.Add(WorkshopSubmission.Create(workshop.Id, me, request.Text, clock.GetUtcNow()));
        }
        else
        {
            existing.Replace(request.Text, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record SubmitWorkshopReviewCommand(Guid CohortId, Guid ReviewId, decimal Score, string? Comment) : ICommand<Unit>;

internal sealed class SubmitWorkshopReviewValidator : AbstractValidator<SubmitWorkshopReviewCommand>
{
    public SubmitWorkshopReviewValidator() => RuleFor(c => c.Comment).MaximumLength(Workshop.CommentMaxLength);
}

internal sealed class SubmitWorkshopReviewHandler(AssessmentAccess access, IWorkshopRepository workshops, ICurrentUser user, IAssessmentUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<SubmitWorkshopReviewCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SubmitWorkshopReviewCommand request, CancellationToken cancellationToken)
    {
        var cohort = await access.TraineeAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var review = await workshops.FindReviewAsync(request.ReviewId, cancellationToken);
        if (review is null || review.ReviewerId != user.Id)
        {
            return WorkshopErrors.ReviewNotFound;
        }

        var workshop = await workshops.FindAsync(review.WorkshopId, cancellationToken);
        if (workshop is null || workshop.CohortId != request.CohortId)
        {
            return WorkshopErrors.ReviewNotFound;
        }

        if (workshop.Status != WorkshopStatus.Review)
        {
            return WorkshopErrors.ReviewClosed;
        }

        var recorded = review.Record(request.Score, request.Comment, clock.GetUtcNow());
        if (!recorded.IsSuccess)
        {
            return recorded.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
