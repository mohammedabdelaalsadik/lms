using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Reports.Application;

public sealed record SearchCourse(Guid Id, string Code, TextDto Title);

public sealed record SearchCohort(Guid Id, TextDto Name);

public sealed record SearchPerson(Guid Id, TextDto Name, string Role);

public sealed record SearchResponse(IReadOnlyList<SearchCourse> Courses, IReadOnlyList<SearchCohort> Cohorts, IReadOnlyList<SearchPerson> People);

public sealed record GlobalSearchQuery(string Term) : IQuery<SearchResponse>;

internal sealed class GlobalSearchValidator : AbstractValidator<GlobalSearchQuery>
{
    public GlobalSearchValidator() => RuleFor(q => q.Term).NotEmpty().MinimumLength(2).MaximumLength(100);
}

/// <summary>Each section shows only what this person could already open elsewhere.</summary>
internal sealed class GlobalSearchHandler(ICourseCatalog courses, ICohortDirectory cohorts, IUserDirectory people, ICurrentUser user)
    : IRequestHandler<GlobalSearchQuery, SearchResponse>
{
    private const int Take = 10;

    public async Task<Result<SearchResponse>> Handle(GlobalSearchQuery request, CancellationToken cancellationToken)
    {
        var term = request.Term.Trim();
        var staff = user.Role is LmsRoles.Admin or LmsRoles.Coordinator or LmsRoles.Auditor;
        var me = user.Id!.Value;

        var found = await courses.SearchAsync(term, !(staff || user.Role == LmsRoles.Instructor), Take, cancellationToken);

        var visible = user.Role switch
        {
            LmsRoles.Trainee => await cohorts.GetEnrolledCohortsAsync(me, cancellationToken),
            LmsRoles.Instructor => await cohorts.GetCohortsAsync(me, cancellationToken),
            LmsRoles.Admin or LmsRoles.Coordinator => await cohorts.GetCohortsAsync(null, cancellationToken),
            _ => [],
        };

        bool Has(string text) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<UserInfo> matches = user.Role is LmsRoles.Admin or LmsRoles.Auditor ? await people.SearchAsync(term, Take, cancellationToken) : [];

        return new SearchResponse(
            [.. found.Select(c => new SearchCourse(c.Id, c.Code, new TextDto(c.TitleAr, c.TitleEn)))],
            [.. visible.Where(c => Has(c.NameAr) || Has(c.NameEn)).Take(Take).Select(c => new SearchCohort(c.Id, new TextDto(c.NameAr, c.NameEn)))],
            [.. matches.Select(p => new SearchPerson(p.Id, new TextDto(p.NameAr, p.NameEn), p.Role))]);
    }
}
