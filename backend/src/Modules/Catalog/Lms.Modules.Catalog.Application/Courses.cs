using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Catalog.Application;

public interface ICourseRepository
{
    Task<Course?> FindAsync(CourseId id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    void Add(Course course);

    Task<PagedResult<CourseResponse>> ListAsync(CourseFilter filter, CancellationToken cancellationToken);
}

public interface ICatalogUnitOfWork : IUnitOfWork;

public sealed record LocalizedTextDto(string Ar, string En);

public sealed record CourseResponse(
    Guid Id,
    string Code,
    LocalizedTextDto Title,
    LocalizedTextDto Description,
    int DurationHours,
    CourseStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    Guid? CategoryId);

public sealed record CourseFilter(CourseStatus? Status, string? Search, int Page, int PageSize, IReadOnlyCollection<Guid>? CategoryIds = null);

internal static class CourseMapping
{
    public static CourseResponse ToResponse(this Course c) => new(
        c.Id.Value,
        c.Code,
        new LocalizedTextDto(c.Title.Ar, c.Title.En),
        new LocalizedTextDto(c.Description.Ar, c.Description.En),
        c.DurationHours,
        c.Status,
        c.CreatedAt,
        c.PublishedAt,
        c.CategoryId);
}

internal static class CourseAccess
{
    /// <summary>Unpublished courses are visible only to the staff who manage the catalog.</summary>
    public static bool CanSeeUnpublished(this ICurrentUser user) =>
        user.Role is LmsRoles.Admin or LmsRoles.Coordinator;
}

public sealed record ListCoursesQuery(CourseStatus? Status, string? Search, int Page = 1, int PageSize = 20, Guid? CategoryId = null)
    : IQuery<PagedResult<CourseResponse>>;

internal sealed class ListCoursesValidator : AbstractValidator<ListCoursesQuery>
{
    public ListCoursesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListCoursesHandler(ICourseRepository courses, ICategoryRepository categories, ICurrentUser user)
    : IRequestHandler<ListCoursesQuery, PagedResult<CourseResponse>>
{
    public async Task<Result<PagedResult<CourseResponse>>> Handle(
        ListCoursesQuery request,
        CancellationToken cancellationToken)
    {
        var status = user.CanSeeUnpublished() ? request.Status : CourseStatus.Published;
        IReadOnlyCollection<Guid>? ids = null;

        // Choosing a category includes everything filed under it.
        if (request.CategoryId is { } category)
        {
            ids = [category, .. CourseCategory.Descendants(category, await categories.ListAsync(cancellationToken))];
        }

        var filter = new CourseFilter(status, request.Search, request.Page, request.PageSize, ids);
        return await courses.ListAsync(filter, cancellationToken);
    }
}

public sealed record GetCourseQuery(Guid Id) : IQuery<CourseResponse>;

internal sealed class GetCourseHandler(ICourseRepository courses, ICurrentUser user)
    : IRequestHandler<GetCourseQuery, CourseResponse>
{
    public async Task<Result<CourseResponse>> Handle(GetCourseQuery request, CancellationToken cancellationToken)
    {
        var course = await courses.FindAsync(new CourseId(request.Id), cancellationToken);

        if (course is null || (course.Status != CourseStatus.Published && !user.CanSeeUnpublished()))
        {
            return CourseErrors.NotFound;
        }

        return course.ToResponse();
    }
}

public sealed record CreateCourseCommand(
    string Code,
    string TitleAr,
    string TitleEn,
    string DescriptionAr,
    string DescriptionEn,
    int DurationHours) : ICommand<CourseResponse>;

internal sealed class CreateCourseValidator : AbstractValidator<CreateCourseCommand>
{
    public CreateCourseValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Course.CodeMaxLength).Matches("^[A-Za-z0-9-]+$");
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Course.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Course.TitleMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(Course.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(Course.DescriptionMaxLength);
        RuleFor(c => c.DurationHours).GreaterThan(0);
    }
}

internal sealed class CreateCourseHandler(ICourseRepository courses, ICatalogUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<CreateCourseCommand, CourseResponse>
{
    public async Task<Result<CourseResponse>> Handle(CreateCourseCommand request, CancellationToken cancellationToken)
    {
        if (await courses.CodeExistsAsync(Course.NormalizeCode(request.Code), cancellationToken))
        {
            return CourseErrors.CodeTaken;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Course.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, Course.DescriptionMaxLength);
        if (!description.IsSuccess)
        {
            return description.Error!;
        }

        var course = Course.Create(request.Code, title.Value, description.Value, request.DurationHours, clock.GetUtcNow());
        if (!course.IsSuccess)
        {
            return course.Error!;
        }

        courses.Add(course.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return course.Value.ToResponse();
    }
}

public sealed record UpdateCourseCommand(
    Guid Id,
    string TitleAr,
    string TitleEn,
    string DescriptionAr,
    string DescriptionEn,
    int DurationHours) : ICommand<CourseResponse>;

internal sealed class UpdateCourseValidator : AbstractValidator<UpdateCourseCommand>
{
    public UpdateCourseValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Course.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Course.TitleMaxLength);
        RuleFor(c => c.DescriptionAr).NotEmpty().MaximumLength(Course.DescriptionMaxLength);
        RuleFor(c => c.DescriptionEn).NotEmpty().MaximumLength(Course.DescriptionMaxLength);
        RuleFor(c => c.DurationHours).GreaterThan(0);
    }
}

internal sealed class UpdateCourseHandler(ICourseRepository courses, ICatalogUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCourseCommand, CourseResponse>
{
    public async Task<Result<CourseResponse>> Handle(UpdateCourseCommand request, CancellationToken cancellationToken)
    {
        var course = await courses.FindAsync(new CourseId(request.Id), cancellationToken);
        if (course is null)
        {
            return CourseErrors.NotFound;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Course.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var description = LocalizedText.Create(request.DescriptionAr, request.DescriptionEn, Course.DescriptionMaxLength);
        if (!description.IsSuccess)
        {
            return description.Error!;
        }

        var updated = course.UpdateDetails(title.Value, description.Value, request.DurationHours);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return course.ToResponse();
    }
}

public sealed record PublishCourseCommand(Guid Id) : ICommand<Unit>;

internal sealed class PublishCourseHandler(ICourseRepository courses, ICatalogUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<PublishCourseCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PublishCourseCommand request, CancellationToken cancellationToken)
    {
        var course = await courses.FindAsync(new CourseId(request.Id), cancellationToken);
        if (course is null)
        {
            return CourseErrors.NotFound;
        }

        var result = course.Publish(clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ArchiveCourseCommand(Guid Id) : ICommand<Unit>;

internal sealed class ArchiveCourseHandler(ICourseRepository courses, ICatalogUnitOfWork unitOfWork)
    : IRequestHandler<ArchiveCourseCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ArchiveCourseCommand request, CancellationToken cancellationToken)
    {
        var course = await courses.FindAsync(new CourseId(request.Id), cancellationToken);
        if (course is null)
        {
            return CourseErrors.NotFound;
        }

        var result = course.Archive();
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
