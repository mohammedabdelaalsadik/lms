using Lms.SharedKernel;

namespace Lms.Modules.Community.Domain;

public readonly record struct WikiPageId(Guid Value);

public static class WikiErrors
{
    public static readonly Error NotFound = Error.NotFound("community.wiki_not_found", "This page was not found.");
    public static readonly Error TitleTaken = Error.Conflict("community.wiki_title_taken", "A page with this title already exists.");
    public static readonly Error Stale = Error.Conflict("community.wiki_stale", "Someone else changed this page. Copy your text, reload and try again.");
    public static readonly Error RevisionNotFound = Error.NotFound("community.wiki_revision_not_found", "This version was not found.");
}

/// <summary>A page the whole cohort writes together. Nothing is overwritten: every save adds a revision.</summary>
public sealed class WikiPage : AggregateRoot<WikiPageId>
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 50000;

    private WikiPage()
    {
    }

    public Guid CohortId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public int Version { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    public static (WikiPage Page, WikiRevision Revision) Create(Guid cohortId, string title, string body, Guid author, DateTimeOffset now)
    {
        var page = new WikiPage { Id = new WikiPageId(Guid.CreateVersion7()), CohortId = cohortId };
        return (page, page.Apply(title, body, author, now));
    }

    /// <summary>Saving against an older version would silently undo someone else's work.</summary>
    public Result<WikiRevision> Edit(string title, string body, int basedOn, Guid editor, DateTimeOffset now) =>
        basedOn != Version ? WikiErrors.Stale : Apply(title, body, editor, now);

    /// <summary>Going back adds a new revision, so the history stays complete.</summary>
    public WikiRevision Restore(WikiRevision revision, Guid editor, DateTimeOffset now) => Apply(revision.Title, revision.Body, editor, now);

    public void Remove(DateTimeOffset now) => RemovedAt ??= now;

    private WikiRevision Apply(string title, string body, Guid editor, DateTimeOffset now)
    {
        Title = title.Trim();
        Body = body;
        Version++;
        UpdatedBy = editor;
        UpdatedAt = now;
        return WikiRevision.Create(Id, Version, Title, body, editor, now);
    }
}

public sealed class WikiRevision
{
    private WikiRevision()
    {
    }

    public Guid Id { get; private set; }
    public WikiPageId PageId { get; private set; }
    public int Version { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public Guid EditorId { get; private set; }
    public DateTimeOffset EditedAt { get; private set; }

    internal static WikiRevision Create(WikiPageId pageId, int version, string title, string body, Guid editor, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        PageId = pageId,
        Version = version,
        Title = title,
        Body = body,
        EditorId = editor,
        EditedAt = now,
    };
}
