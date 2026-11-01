using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Community.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Community.Application;

public interface IWikiRepository
{
    Task<WikiPage?> FindAsync(WikiPageId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WikiPage>> ListAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<bool> TitleTakenAsync(Guid cohortId, string title, WikiPageId? except, CancellationToken cancellationToken);

    Task<IReadOnlyList<WikiRevision>> ListRevisionsAsync(WikiPageId id, CancellationToken cancellationToken);

    void Add(WikiPage page);

    void Add(WikiRevision revision);
}

public sealed record WikiSummary(Guid Id, string Title, int Version, TextDto? EditedBy, DateTimeOffset UpdatedAt);

public sealed record WikiList(TextDto CohortName, bool CanModerate, IReadOnlyList<WikiSummary> Pages);

public sealed record WikiHistoryEntry(int Version, TextDto? EditedBy, DateTimeOffset EditedAt);

public sealed record WikiDetail(Guid Id, string Title, string Body, int Version, bool CanModerate, IReadOnlyList<WikiHistoryEntry> History);

public sealed record WikiRevisionDetail(int Version, string Title, string Body);

internal sealed class WikiNames(IUserDirectory directory)
{
    public async Task<Func<Guid, TextDto?>> ForAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var people = await directory.GetAsync([.. ids.Distinct()], cancellationToken);
        return id => people.TryGetValue(id, out var person) ? new TextDto(person.NameAr, person.NameEn) : null;
    }
}

public sealed class WikiPages(ForumAccess access, IWikiRepository wiki)
{
    /// <summary>Checks membership and that the page belongs to this cohort, so ids cannot reach across cohorts.</summary>
    public async Task<Result<(Membership Member, WikiPage Page)>> OpenAsync(Guid cohortId, Guid pageId, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(cohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var page = await wiki.FindAsync(new WikiPageId(pageId), cancellationToken);
        return page is null || page.CohortId != cohortId || page.RemovedAt is not null
            ? WikiErrors.NotFound
            : (member.Value, page);
    }
}

public sealed record ListWikiQuery(Guid CohortId) : IQuery<WikiList>;

internal sealed class ListWikiHandler(ForumAccess access, IWikiRepository wiki, IUserDirectory directory) : IRequestHandler<ListWikiQuery, WikiList>
{
    public async Task<Result<WikiList>> Handle(ListWikiQuery request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var pages = await wiki.ListAsync(request.CohortId, cancellationToken);
        var names = await new WikiNames(directory).ForAsync(pages.Select(p => p.UpdatedBy), cancellationToken);
        var cohort = member.Value.Cohort;

        return new WikiList(
            new TextDto(cohort.NameAr, cohort.NameEn),
            member.Value.IsModerator,
            [.. pages.Select(p => new WikiSummary(p.Id.Value, p.Title, p.Version, names(p.UpdatedBy), p.UpdatedAt))]);
    }
}

public sealed record GetWikiPageQuery(Guid CohortId, Guid PageId) : IQuery<WikiDetail>;

internal sealed class GetWikiPageHandler(WikiPages pages, IWikiRepository wiki, IUserDirectory directory) : IRequestHandler<GetWikiPageQuery, WikiDetail>
{
    public async Task<Result<WikiDetail>> Handle(GetWikiPageQuery request, CancellationToken cancellationToken)
    {
        var opened = await pages.OpenAsync(request.CohortId, request.PageId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (member, page) = opened.Value;
        var revisions = await wiki.ListRevisionsAsync(page.Id, cancellationToken);
        var names = await new WikiNames(directory).ForAsync(revisions.Select(r => r.EditorId), cancellationToken);

        return new WikiDetail(page.Id.Value, page.Title, page.Body, page.Version, member.IsModerator, [.. revisions.Select(r => new WikiHistoryEntry(r.Version, names(r.EditorId), r.EditedAt))]);
    }
}

public sealed record GetWikiRevisionQuery(Guid CohortId, Guid PageId, int Version) : IQuery<WikiRevisionDetail>;

internal sealed class GetWikiRevisionHandler(WikiPages pages, IWikiRepository wiki) : IRequestHandler<GetWikiRevisionQuery, WikiRevisionDetail>
{
    public async Task<Result<WikiRevisionDetail>> Handle(GetWikiRevisionQuery request, CancellationToken cancellationToken)
    {
        var opened = await pages.OpenAsync(request.CohortId, request.PageId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var revision = (await wiki.ListRevisionsAsync(opened.Value.Page.Id, cancellationToken)).FirstOrDefault(r => r.Version == request.Version);
        return revision is null ? WikiErrors.RevisionNotFound : new WikiRevisionDetail(revision.Version, revision.Title, revision.Body);
    }
}

public sealed record CreateWikiPageCommand(Guid CohortId, string Title, string Body) : ICommand<Guid>;

internal sealed class CreateWikiPageValidator : AbstractValidator<CreateWikiPageCommand>
{
    public CreateWikiPageValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(WikiPage.TitleMaxLength);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(WikiPage.BodyMaxLength);
    }
}

internal sealed class CreateWikiPageHandler(ForumAccess access, IWikiRepository wiki, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<CreateWikiPageCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateWikiPageCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        if (await wiki.TitleTakenAsync(request.CohortId, request.Title.Trim(), null, cancellationToken))
        {
            return WikiErrors.TitleTaken;
        }

        var (page, revision) = WikiPage.Create(request.CohortId, request.Title, request.Body, user.Id!.Value, clock.GetUtcNow());
        wiki.Add(page);
        wiki.Add(revision);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return page.Id.Value;
    }
}

public sealed record EditWikiPageCommand(Guid CohortId, Guid PageId, string Title, string Body, int Version) : ICommand<Unit>;

internal sealed class EditWikiPageValidator : AbstractValidator<EditWikiPageCommand>
{
    public EditWikiPageValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(WikiPage.TitleMaxLength);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(WikiPage.BodyMaxLength);
    }
}

internal sealed class EditWikiPageHandler(WikiPages pages, IWikiRepository wiki, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<EditWikiPageCommand, Unit>
{
    public async Task<Result<Unit>> Handle(EditWikiPageCommand request, CancellationToken cancellationToken)
    {
        var opened = await pages.OpenAsync(request.CohortId, request.PageId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var page = opened.Value.Page;
        if (await wiki.TitleTakenAsync(request.CohortId, request.Title.Trim(), page.Id, cancellationToken))
        {
            return WikiErrors.TitleTaken;
        }

        var edited = page.Edit(request.Title, request.Body, request.Version, user.Id!.Value, clock.GetUtcNow());
        if (!edited.IsSuccess)
        {
            return edited.Error!;
        }

        wiki.Add(edited.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RestoreWikiRevisionCommand(Guid CohortId, Guid PageId, int Version) : ICommand<Unit>;

internal sealed class RestoreWikiRevisionHandler(WikiPages pages, IWikiRepository wiki, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RestoreWikiRevisionCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RestoreWikiRevisionCommand request, CancellationToken cancellationToken)
    {
        var opened = await pages.OpenAsync(request.CohortId, request.PageId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (member, page) = opened.Value;
        if (!member.IsModerator)
        {
            return CommunityErrors.NotAllowed;
        }

        var revision = (await wiki.ListRevisionsAsync(page.Id, cancellationToken)).FirstOrDefault(r => r.Version == request.Version);
        if (revision is null)
        {
            return WikiErrors.RevisionNotFound;
        }

        wiki.Add(page.Restore(revision, user.Id!.Value, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RemoveWikiPageCommand(Guid CohortId, Guid PageId) : ICommand<Unit>;

internal sealed class RemoveWikiPageHandler(WikiPages pages, ICommunityUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RemoveWikiPageCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveWikiPageCommand request, CancellationToken cancellationToken)
    {
        var opened = await pages.OpenAsync(request.CohortId, request.PageId, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        if (!opened.Value.Member.IsModerator)
        {
            return CommunityErrors.NotAllowed;
        }

        opened.Value.Page.Remove(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
