using Lms.SharedKernel;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Catalog.Infrastructure;

internal sealed class CourseCatalog(CatalogDbContext db, TimeProvider clock) : ICourseCatalog
{
    public async Task<CourseDraft?> GetDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == new CourseId(id), cancellationToken);
        return course is null ? null : new CourseDraft(course.Code, course.Title.Ar, course.Title.En, course.Description.Ar, course.Description.En, course.DurationHours);
    }

    public async Task<bool> CreateDraftAsync(Guid id, CourseDraft draft, CancellationToken cancellationToken)
    {
        var code = Course.NormalizeCode(draft.Code);
        var title = LocalizedText.Create(draft.TitleAr, draft.TitleEn, Course.TitleMaxLength);
        var description = LocalizedText.Create(draft.DescriptionAr, draft.DescriptionEn, Course.DescriptionMaxLength);

        if (!title.IsSuccess || !description.IsSuccess || await db.Courses.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return false;
        }

        var course = Course.Create(code, title.Value, description.Value, draft.DurationHours, clock.GetUtcNow(), id);
        if (!course.IsSuccess)
        {
            return false;
        }

        db.Courses.Add(course.Value);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CourseInfo>> SearchAsync(string term, bool onlyPublished, int take, CancellationToken cancellationToken) =>
        await db.Courses.AsNoTracking()
            .Where(c => (!onlyPublished || c.Status == CourseStatus.Published) && (c.Code.Contains(term) || c.Title.Ar.Contains(term) || c.Title.En.Contains(term)))
            .OrderBy(c => c.Code)
            .Take(take)
            .Select(c => new CourseInfo(c.Id.Value, c.Code, c.Title.Ar, c.Title.En, c.Status == CourseStatus.Published))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, CourseInfo>> GetAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var courseIds = ids.Select(id => new CourseId(id)).ToList();

        return await db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .Select(c => new CourseInfo(c.Id.Value, c.Code, c.Title.Ar, c.Title.En, c.Status == CourseStatus.Published))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }
}
