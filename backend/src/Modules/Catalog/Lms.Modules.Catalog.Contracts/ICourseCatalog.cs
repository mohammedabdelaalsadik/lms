namespace Lms.Modules.Catalog.Contracts;

public sealed record CourseInfo(Guid Id, string Code, string TitleAr, string TitleEn, bool IsPublished);

public sealed record CourseDraft(string Code, string TitleAr, string TitleEn, string DescriptionAr, string DescriptionEn, int DurationHours);

/// <summary>What other modules may ask the Catalog module. Its domain stays private.</summary>
public interface ICourseCatalog
{
    Task<IReadOnlyDictionary<Guid, CourseInfo>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<CourseDraft?> GetDraftAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>False when the code is taken or the details break a course rule.</summary>
    Task<bool> CreateDraftAsync(Guid id, CourseDraft draft, CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseInfo>> SearchAsync(string term, bool onlyPublished, int take, CancellationToken cancellationToken);
}
