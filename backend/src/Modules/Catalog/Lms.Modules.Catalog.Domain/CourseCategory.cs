using Lms.SharedKernel;

namespace Lms.Modules.Catalog.Domain;

public readonly record struct CourseCategoryId(Guid Value);

public static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("catalog.category.not_found", "Category was not found.");
    public static readonly Error InvalidParent = Error.Validation("catalog.category.invalid_parent", "A category cannot sit inside itself.");
    public static readonly Error NotEmpty = Error.Conflict("catalog.category.not_empty", "Move its courses and sub-categories first.");
}

public sealed class CourseCategory : AggregateRoot<CourseCategoryId>
{
    public const int NameMaxLength = 150;

    private CourseCategory()
    {
    }

    public LocalizedText Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }

    public static CourseCategory Create(LocalizedText name, Guid? parentId) => new()
    {
        Id = new CourseCategoryId(Guid.CreateVersion7()),
        Name = name,
        ParentId = parentId,
    };

    /// <summary><paramref name="descendants"/> are this category's own sub-categories at any depth.</summary>
    public Result Update(LocalizedText name, Guid? parentId, IReadOnlyCollection<Guid> descendants)
    {
        if (parentId == Id.Value || (parentId is { } parent && descendants.Contains(parent)))
        {
            return CategoryErrors.InvalidParent;
        }

        Name = name;
        ParentId = parentId;
        return Result.Success();
    }

    public static IReadOnlyCollection<Guid> Descendants(Guid root, IReadOnlyCollection<CourseCategory> all)
    {
        var found = new HashSet<Guid>();
        var waiting = new Queue<Guid>([root]);

        while (waiting.TryDequeue(out var current))
        {
            foreach (var child in all.Where(c => c.ParentId == current && found.Add(c.Id.Value)))
            {
                waiting.Enqueue(child.Id.Value);
            }
        }

        return found;
    }
}
