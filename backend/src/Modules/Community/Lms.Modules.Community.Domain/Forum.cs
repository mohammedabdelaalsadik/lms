using Lms.SharedKernel;

namespace Lms.Modules.Community.Domain;

public readonly record struct TopicId(Guid Value);

public readonly record struct PostId(Guid Value);

public sealed record TopicReplied(Guid CohortId, Guid TopicId, Guid PostId, Guid TopicAuthorId, Guid ReplierId, string TopicTitle) : IDomainEvent;

public static class CommunityErrors
{
    public static readonly Error CohortNotFound = Error.NotFound("community.cohort_not_found", "Cohort was not found.");
    public static readonly Error TopicNotFound = Error.NotFound("community.topic_not_found", "Topic was not found.");
    public static readonly Error PostNotFound = Error.NotFound("community.post_not_found", "Post was not found.");
    public static readonly Error NotAMember = Error.Forbidden("community.not_a_member", "This forum belongs to a cohort you are not part of.");
    public static readonly Error NotAllowed = Error.Forbidden("community.not_allowed", "You cannot do this.");
    public static readonly Error TopicLocked = Error.Conflict("community.topic_locked", "This topic is locked.");
    public static readonly Error EmptyText = Error.Validation("community.empty_text", "Write something first.");
    public static readonly Error CannotMessageSelf = Error.Validation("community.cannot_message_self", "You cannot message yourself.");
    public static readonly Error CannotMessageUser = Error.Forbidden("community.cannot_message_user", "You can only message people in your cohorts or staff.");
    public static readonly Error UserNotFound = Error.NotFound("community.user_not_found", "User was not found.");
}

/// <summary>A discussion in one cohort. Removal hides it but keeps it on record.</summary>
public sealed class ForumTopic : AggregateRoot<TopicId>
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 8000;

    private ForumTopic()
    {
    }

    public Guid CohortId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid? GroupId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int ReplyCount { get; private set; }
    public bool IsPinned { get; private set; }
    public bool IsLocked { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid? RemovedBy { get; private set; }

    public static Result<ForumTopic> Start(Guid cohortId, Guid authorId, string? title, string? body, DateTimeOffset now, Guid? groupId = null)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            return CommunityErrors.EmptyText;
        }

        return new ForumTopic
        {
            Id = new TopicId(Guid.CreateVersion7()),
            CohortId = cohortId,
            AuthorId = authorId,
            GroupId = groupId,
            Title = title.Trim(),
            Body = body.Trim(),
            CreatedAt = now,
            LastActivityAt = now,
        };
    }

    /// <summary>Moderators may still answer in a locked topic, for example to close it with an explanation.</summary>
    public Result<ForumPost> Reply(Guid authorId, string? body, bool isModerator, DateTimeOffset now)
    {
        if (RemovedAt is not null)
        {
            return CommunityErrors.TopicNotFound;
        }

        if (IsLocked && !isModerator)
        {
            return CommunityErrors.TopicLocked;
        }

        var post = ForumPost.Write(Id, authorId, body, now);
        if (!post.IsSuccess)
        {
            return post;
        }

        ReplyCount++;
        LastActivityAt = now;

        if (authorId != AuthorId)
        {
            Raise(new TopicReplied(CohortId, Id.Value, post.Value.Id.Value, AuthorId, authorId, Title));
        }

        return post;
    }

    public void SetPinned(bool pinned) => IsPinned = pinned;

    public void SetLocked(bool locked) => IsLocked = locked;

    public void Remove(Guid removedBy, DateTimeOffset now)
    {
        RemovedAt ??= now;
        RemovedBy ??= removedBy;
    }

    public void PostRemoved() => ReplyCount = Math.Max(0, ReplyCount - 1);
}

public sealed class ForumPost : AggregateRoot<PostId>
{
    private ForumPost()
    {
    }

    public TopicId TopicId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public Guid? RemovedBy { get; private set; }

    internal static Result<ForumPost> Write(TopicId topicId, Guid authorId, string? body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return CommunityErrors.EmptyText;
        }

        return new ForumPost
        {
            Id = new PostId(Guid.CreateVersion7()),
            TopicId = topicId,
            AuthorId = authorId,
            Body = body.Trim(),
            CreatedAt = now,
        };
    }

    public void Remove(Guid removedBy, DateTimeOffset now)
    {
        RemovedAt ??= now;
        RemovedBy ??= removedBy;
    }
}
