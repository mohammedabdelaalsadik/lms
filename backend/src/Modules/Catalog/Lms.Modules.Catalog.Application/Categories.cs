using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Catalog.Application;

public interface ICategoryRepository
{
    Task<IReadOnlyList<CourseCategory>> ListAsync(CancellationToken cancellationToken);

    Task<CourseCategory?> FindAsync(CourseCategoryId id, CancellationToken cancellationToken);

    Task<bool> HasCoursesAsync(Guid categoryId, CancellationToken cancellationToken);

    void Add(CourseCategory category);

    void Remove(CourseCategory category);
}

public sealed record CategoryResponse(Guid Id, LocalizedTextDto Name, Guid? ParentId);

public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryResponse>>;

internal sealed class ListCategoriesHandler(ICategoryRepository categories) : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(cancellationToken);
        IReadOnlyList<CategoryResponse> items = [.. all.Select(c => new CategoryResponse(c.Id.Value, new LocalizedTextDto(c.Name.Ar, c.Name.En), c.ParentId))];
        return Result<IReadOnlyList<CategoryResponse>>.Success(items);
    }
}

public sealed record SaveCategoryCommand(Guid? Id, string NameAr, string NameEn, Guid? ParentId) : ICommand<Guid>;

internal sealed class SaveCategoryValidator : AbstractValidator<SaveCategoryCommand>
{
    public SaveCategoryValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(CourseCategory.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(CourseCategory.NameMaxLength);
    }
}

internal sealed class SaveCategoryHandler(ICategoryRepository categories, ICatalogUnitOfWork unitOfWork) : IRequestHandler<SaveCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveCategoryCommand request, CancellationToken cancellationToken)
    {
        var name = LocalizedText.Create(request.NameAr, request.NameEn, CourseCategory.NameMaxLength);
        if (!name.IsSuccess)
        {
            return name.Error!;
        }

        var all = await categories.ListAsync(cancellationToken);

        if (request.ParentId is { } parent && all.All(c => c.Id.Value != parent))
        {
            return CategoryErrors.InvalidParent;
        }

        if (request.Id is not { } id)
        {
            var created = CourseCategory.Create(name.Value, request.ParentId);
            categories.Add(created);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return created.Id.Value;
        }

        var category = await categories.FindAsync(new CourseCategoryId(id), cancellationToken);
        if (category is null)
        {
            return CategoryErrors.NotFound;
        }

        var updated = category.Update(name.Value, request.ParentId, CourseCategory.Descendants(id, all));
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return id;
    }
}

public sealed record DeleteCategoryCommand(Guid Id) : ICommand<Unit>;

internal sealed class DeleteCategoryHandler(ICategoryRepository categories, ICatalogUnitOfWork unitOfWork) : IRequestHandler<DeleteCategoryCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(new CourseCategoryId(request.Id), cancellationToken);
        if (category is null)
        {
            return CategoryErrors.NotFound;
        }

        var all = await categories.ListAsync(cancellationToken);

        if (all.Any(c => c.ParentId == request.Id) || await categories.HasCoursesAsync(request.Id, cancellationToken))
        {
            return CategoryErrors.NotEmpty;
        }

        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record MoveCourseCommand(Guid CourseId, Guid? CategoryId) : ICommand<Unit>;

internal sealed class MoveCourseHandler(ICourseRepository courses, ICategoryRepository categories, ICatalogUnitOfWork unitOfWork) : IRequestHandler<MoveCourseCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MoveCourseCommand request, CancellationToken cancellationToken)
    {
        var course = await courses.FindAsync(new CourseId(request.CourseId), cancellationToken);
        if (course is null)
        {
            return CourseErrors.NotFound;
        }

        if (request.CategoryId is { } id && await categories.FindAsync(new CourseCategoryId(id), cancellationToken) is null)
        {
            return CategoryErrors.NotFound;
        }

        course.MoveTo(request.CategoryId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
