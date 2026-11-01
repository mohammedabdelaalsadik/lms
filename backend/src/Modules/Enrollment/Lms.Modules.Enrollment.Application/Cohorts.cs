using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Enrollment.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Enrollment.Application;

public interface IEnrollmentUnitOfWork : IUnitOfWork;

public sealed record CohortFilter(bool OnlyEnrollable, DateOnly Today, Guid? ViewerId, string? Search, CohortStatus? Status, int Page, int PageSize);

/// <summary>Flat row read from storage, before names are added from other modules.</summary>
public sealed record CohortRow(
    Guid Id,
    Guid CourseId,
    string NameAr,
    string NameEn,
    string Branch,
    Guid InstructorId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Capacity,
    int EnrolledCount,
    int PendingCount,
    bool RequiresApproval,
    CohortStatus Status,
    EnrollmentStatus? MyStatus);

public sealed record MyEnrollmentRow(Guid EnrollmentId, EnrollmentStatus Status, string? RejectionReason, DateTimeOffset RequestedAt, CohortRow Cohort);

public interface ICohortRepository
{
    Task<Cohort?> FindAsync(CohortId id, CancellationToken cancellationToken);

    void Add(Cohort cohort);

    Task<PagedResult<CohortRow>> ListAsync(CohortFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<MyEnrollmentRow>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken);
}

public sealed record TextDto(string Ar, string En);

public sealed record CohortResponse(
    Guid Id,
    Guid CourseId,
    string CourseCode,
    TextDto CourseTitle,
    TextDto Name,
    string Branch,
    Guid InstructorId,
    TextDto InstructorName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Capacity,
    int SeatsLeft,
    int PendingCount,
    bool RequiresApproval,
    CohortStatus Status,
    EnrollmentStatus? MyStatus);

public sealed record RosterEntry(
    Guid EnrollmentId,
    Guid TraineeId,
    TextDto TraineeName,
    string MaskedNationalId,
    EnrollmentStatus Status,
    DateTimeOffset RequestedAt,
    string? RejectionReason);

public sealed record CohortRosterResponse(CohortResponse Cohort, IReadOnlyList<RosterEntry> Entries);

public sealed record MyEnrollmentResponse(
    Guid EnrollmentId,
    EnrollmentStatus Status,
    string? RejectionReason,
    DateTimeOffset RequestedAt,
    CohortResponse Cohort);

/// <summary>Adds course and instructor names from their owning modules, in one query each.</summary>
public sealed class CohortEnricher(ICourseCatalog catalog, IUserDirectory directory)
{
    public async Task<IReadOnlyList<CohortResponse>> EnrichAsync(
        IReadOnlyCollection<CohortRow> rows,
        CancellationToken cancellationToken)
    {
        var courses = await catalog.GetAsync([.. rows.Select(r => r.CourseId).Distinct()], cancellationToken);
        var instructors = await directory.GetAsync([.. rows.Select(r => r.InstructorId).Distinct()], cancellationToken);

        return [.. rows.Select(r =>
        {
            var course = courses.GetValueOrDefault(r.CourseId);
            var instructor = instructors.GetValueOrDefault(r.InstructorId);

            return new CohortResponse(
                r.Id,
                r.CourseId,
                course?.Code ?? "",
                new TextDto(course?.TitleAr ?? "", course?.TitleEn ?? ""),
                new TextDto(r.NameAr, r.NameEn),
                r.Branch,
                r.InstructorId,
                new TextDto(instructor?.NameAr ?? "", instructor?.NameEn ?? ""),
                r.StartsOn,
                r.EndsOn,
                r.Capacity,
                r.Capacity - r.EnrolledCount,
                r.PendingCount,
                r.RequiresApproval,
                r.Status,
                r.MyStatus);
        })];
    }
}

internal static class CohortAccess
{
    public static bool IsManager(this ICurrentUser user) => user.Role is LmsRoles.Admin or LmsRoles.Coordinator;
}

public sealed record ListCohortsQuery(string? Search, CohortStatus? Status, int Page = 1, int PageSize = 20) : IQuery<PagedResult<CohortResponse>>;

internal sealed class ListCohortsValidator : AbstractValidator<ListCohortsQuery>
{
    public ListCohortsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListCohortsHandler(ICohortRepository cohorts, CohortEnricher enricher, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<ListCohortsQuery, PagedResult<CohortResponse>>
{
    public async Task<Result<PagedResult<CohortResponse>>> Handle(ListCohortsQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var filter = new CohortFilter(user.Role == LmsRoles.Trainee, today, user.Id, request.Search, request.Status, request.Page, request.PageSize);

        var page = await cohorts.ListAsync(filter, cancellationToken);
        var items = await enricher.EnrichAsync(page.Items, cancellationToken);

        return new PagedResult<CohortResponse>(items, page.Total, page.Page, page.PageSize);
    }
}

public sealed record GetCohortRosterQuery(Guid CohortId) : IQuery<CohortRosterResponse>;

internal sealed class GetCohortRosterHandler(
    ICohortRepository cohorts,
    CohortEnricher enricher,
    IUserDirectory directory,
    ICurrentUser user) : IRequestHandler<GetCohortRosterQuery, CohortRosterResponse>
{
    public async Task<Result<CohortRosterResponse>> Handle(GetCohortRosterQuery request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        if (!user.IsManager() && cohort.InstructorId != user.Id)
        {
            return EnrollmentErrors.Forbidden;
        }

        var trainees = await directory.GetAsync([.. cohort.Enrollments.Select(e => e.TraineeId)], cancellationToken);

        var entries = cohort.Enrollments
            .OrderBy(e => e.Status)
            .ThenBy(e => e.RequestedAt)
            .Select(e =>
            {
                var trainee = trainees.GetValueOrDefault(e.TraineeId);
                return new RosterEntry(
                    e.Id.Value,
                    e.TraineeId,
                    new TextDto(trainee?.NameAr ?? "", trainee?.NameEn ?? ""),
                    trainee?.MaskedNationalId ?? "",
                    e.Status,
                    e.RequestedAt,
                    e.RejectionReason);
            })
            .ToList();

        var pending = cohort.Enrollments.Count(e => e.Status == EnrollmentStatus.Pending);
        var row = new CohortRow(
            cohort.Id.Value,
            cohort.CourseId,
            cohort.Name.Ar,
            cohort.Name.En,
            cohort.Branch,
            cohort.InstructorId,
            cohort.StartsOn,
            cohort.EndsOn,
            cohort.Capacity,
            cohort.EnrolledCount,
            pending,
            cohort.RequiresApproval,
            cohort.Status,
            null);

        var enriched = await enricher.EnrichAsync([row], cancellationToken);
        return new CohortRosterResponse(enriched[0], entries);
    }
}

public sealed record CreateCohortCommand(
    Guid CourseId,
    string NameAr,
    string NameEn,
    string Branch,
    Guid InstructorId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Capacity,
    bool RequiresApproval) : ICommand<Guid>;

internal sealed class CreateCohortValidator : AbstractValidator<CreateCohortCommand>
{
    public CreateCohortValidator()
    {
        RuleFor(c => c.CourseId).NotEmpty();
        RuleFor(c => c.InstructorId).NotEmpty();
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(Cohort.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(Cohort.NameMaxLength);
        RuleFor(c => c.Branch).NotEmpty().MaximumLength(Cohort.BranchMaxLength);
        RuleFor(c => c.Capacity).GreaterThan(0);
    }
}

internal sealed class CreateCohortHandler(
    ICohortRepository cohorts,
    ICourseCatalog catalog,
    IUserDirectory directory,
    IEnrollmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CreateCohortCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCohortCommand request, CancellationToken cancellationToken)
    {
        var courses = await catalog.GetAsync([request.CourseId], cancellationToken);
        if (!courses.TryGetValue(request.CourseId, out var course) || !course.IsPublished)
        {
            return EnrollmentErrors.CourseNotPublished;
        }

        var instructors = await directory.GetAsync([request.InstructorId], cancellationToken);
        if (!instructors.TryGetValue(request.InstructorId, out var instructor) || instructor.Role != LmsRoles.Instructor)
        {
            return EnrollmentErrors.InvalidInstructor;
        }

        var name = LocalizedText.Create(request.NameAr, request.NameEn, Cohort.NameMaxLength);
        if (!name.IsSuccess)
        {
            return name.Error!;
        }

        var cohort = Cohort.Create(
            request.CourseId,
            name.Value,
            request.Branch,
            request.InstructorId,
            request.StartsOn,
            request.EndsOn,
            request.Capacity,
            request.RequiresApproval,
            clock.GetUtcNow());

        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        cohorts.Add(cohort.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return cohort.Value.Id.Value;
    }
}

public sealed record CloseCohortCommand(Guid CohortId) : ICommand<Unit>;

internal sealed class CloseCohortHandler(ICohortRepository cohorts, IEnrollmentUnitOfWork unitOfWork)
    : IRequestHandler<CloseCohortCommand, Unit>
{
    public async Task<Result<Unit>> Handle(CloseCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        var result = cohort.Close();
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record EnrollInCohortCommand(Guid CohortId) : ICommand<EnrollmentStatus>;

internal sealed class EnrollInCohortHandler(
    ICohortRepository cohorts,
    ICurrentUser user,
    IEnrollmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<EnrollInCohortCommand, EnrollmentStatus>
{
    public async Task<Result<EnrollmentStatus>> Handle(EnrollInCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        var enrollment = cohort.Enroll(user.Id!.Value, clock.GetUtcNow());
        if (!enrollment.IsSuccess)
        {
            return enrollment.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return enrollment.Value.Status;
    }
}

public sealed record WithdrawFromCohortCommand(Guid CohortId) : ICommand<Unit>;

internal sealed class WithdrawFromCohortHandler(
    ICohortRepository cohorts,
    ICurrentUser user,
    IEnrollmentUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<WithdrawFromCohortCommand, Unit>
{
    public async Task<Result<Unit>> Handle(WithdrawFromCohortCommand request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        var result = cohort.Withdraw(user.Id!.Value, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ApproveEnrollmentCommand(Guid CohortId, Guid EnrollmentId) : ICommand<Unit>;

internal sealed class ApproveEnrollmentHandler(ICohortRepository cohorts, IEnrollmentUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ApproveEnrollmentCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ApproveEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        var result = cohort.Approve(new EnrollmentId(request.EnrollmentId), clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RejectEnrollmentCommand(Guid CohortId, Guid EnrollmentId, string Reason) : ICommand<Unit>;

internal sealed class RejectEnrollmentValidator : AbstractValidator<RejectEnrollmentCommand>
{
    public RejectEnrollmentValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(CohortEnrollment.ReasonMaxLength);
}

internal sealed class RejectEnrollmentHandler(ICohortRepository cohorts, IEnrollmentUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<RejectEnrollmentCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RejectEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(new CohortId(request.CohortId), cancellationToken);
        if (cohort is null)
        {
            return EnrollmentErrors.CohortNotFound;
        }

        var result = cohort.Reject(new EnrollmentId(request.EnrollmentId), request.Reason, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ListMyEnrollmentsQuery : IQuery<IReadOnlyList<MyEnrollmentResponse>>;

internal sealed class ListMyEnrollmentsHandler(ICohortRepository cohorts, CohortEnricher enricher, ICurrentUser user)
    : IRequestHandler<ListMyEnrollmentsQuery, IReadOnlyList<MyEnrollmentResponse>>
{
    public async Task<Result<IReadOnlyList<MyEnrollmentResponse>>> Handle(ListMyEnrollmentsQuery request, CancellationToken cancellationToken)
    {
        var rows = await cohorts.ListForTraineeAsync(user.Id!.Value, cancellationToken);
        var enriched = await enricher.EnrichAsync([.. rows.Select(r => r.Cohort)], cancellationToken);

        IReadOnlyList<MyEnrollmentResponse> items = [.. rows.Zip(enriched, (row, cohort) =>
            new MyEnrollmentResponse(row.EnrollmentId, row.Status, row.RejectionReason, row.RequestedAt, cohort))];

        return Result<IReadOnlyList<MyEnrollmentResponse>>.Success(items);
    }
}
