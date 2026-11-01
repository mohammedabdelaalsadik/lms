using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Community.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Community.Application;

public interface ICommunityUnitOfWork : IUnitOfWork;

public sealed record TopicFilter(Guid CohortId, string? Search, int Page, int PageSize, bool AllGroups, Guid? GroupId);

public interface IForumRepository
{
    Task<ForumTopic?> FindTopicAsync(TopicId id, CancellationToken cancellationToken);

    Task<ForumPost?> FindPostAsync(PostId id, CancellationToken cancellationToken);

    Task<PagedResult<ForumTopic>> ListTopicsAsync(TopicFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<ForumPost>> ListPostsAsync(TopicId topicId, CancellationToken cancellationToken);

    void Add(ForumTopic topic);

    void Add(ForumPost post);
}

public sealed record TextDto(string Ar, string En);

public sealed record Membership(CohortInfo Cohort, bool IsModerator, Guid? GroupId)
{
    /// <summary>A group's topics stay within that group. Moderators see them all.</summary>
    public bool Sees(ForumTopic topic) => IsModerator || topic.GroupId is null || topic.GroupId == GroupId;
}

/// <summary>A cohort's forum is for its trainees, its instructor and the staff who manage cohorts.</summary>
public sealed class ForumAccess(ICohortDirectory cohorts, ICurrentUser user)
{
    public async Task<Result<Membership>> EnterAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(cohortId, cancellationToken);
        if (cohort is null)
        {
            return CommunityErrors.CohortNotFound;
        }

        var moderator = user.Role is LmsRoles.Admin or LmsRoles.Coordinator || cohort.InstructorId == user.Id;

        if (moderator)
        {
            return new Membership(cohort, true, null);
        }

        var enrolled = user.Id is { } id && await cohorts.IsEnrolledAsync(cohortId, id, cancellationToken);
        return enrolled ? new Membership(cohort, false, await cohorts.GetGroupOfAsync(cohortId, user.Id!.Value, cancellationToken)) : CommunityErrors.NotAMember;
    }
}

public sealed record ForumCohort(Guid CohortId, TextDto Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record ListForumsQuery : IQuery<IReadOnlyList<ForumCohort>>;

internal sealed class ListForumsHandler(ICohortDirectory cohorts, ICurrentUser user)
    : IRequestHandler<ListForumsQuery, IReadOnlyList<ForumCohort>>
{
    public async Task<Result<IReadOnlyList<ForumCohort>>> Handle(ListForumsQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        IReadOnlyList<CohortInfo> found = user.Role switch
        {
            LmsRoles.Admin or LmsRoles.Coordinator => await cohorts.GetCohortsAsync(null, cancellationToken),
            LmsRoles.Instructor => await cohorts.GetCohortsAsync(me, cancellationToken),
            LmsRoles.Trainee => await cohorts.GetEnrolledCohortsAsync(me, cancellationToken),
            _ => [],
        };

        IReadOnlyList<ForumCohort> items = [.. found.Select(c => new ForumCohort(c.Id, new TextDto(c.NameAr, c.NameEn), c.StartsOn, c.EndsOn))];
        return Result<IReadOnlyList<ForumCohort>>.Success(items);
    }
}

public sealed record TopicSummary(
    Guid Id,
    string Title,
    TextDto AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int ReplyCount,
    bool IsPinned,
    bool IsLocked,
    Guid? GroupId);

public sealed record TopicGroup(Guid Id, string Name);

public sealed record TopicListResponse(TextDto CohortName, bool CanModerate, IReadOnlyList<TopicGroup> Groups, PagedResult<TopicSummary> Topics);

public sealed record ListTopicsQuery(Guid CohortId, string? Search, int Page = 1, int PageSize = 20) : IQuery<TopicListResponse>;

internal sealed class ListTopicsValidator : AbstractValidator<ListTopicsQuery>
{
    public ListTopicsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListTopicsHandler(ForumAccess access, IForumRepository forums, IUserDirectory directory, ICohortDirectory cohorts)
    : IRequestHandler<ListTopicsQuery, TopicListResponse>
{
    public async Task<Result<TopicListResponse>> Handle(ListTopicsQuery request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var page = await forums.ListTopicsAsync(new TopicFilter(request.CohortId, request.Search, request.Page, request.PageSize, member.Value.IsModerator, member.Value.GroupId), cancellationToken);
        var groups = (await cohorts.GetGroupsAsync(request.CohortId, cancellationToken))
            .Where(g => member.Value.IsModerator || g.Id == member.Value.GroupId)
            .Select(g => new TopicGroup(g.Id, g.Name))
            .ToList();
        var names = await directory.GetAsync([.. page.Items.Select(t => t.AuthorId).Distinct()], cancellationToken);

        return new TopicListResponse(
            new TextDto(member.Value.Cohort.NameAr, member.Value.Cohort.NameEn),
            member.Value.IsModerator,
            groups,
            new PagedResult<TopicSummary>(
                [.. page.Items.Select(t => new TopicSummary(
                    t.Id.Value,
                    t.Title,
                    Names.Of(names, t.AuthorId),
                    t.CreatedAt,
                    t.LastActivityAt,
                    t.ReplyCount,
                    t.IsPinned,
                    t.IsLocked,
                    t.GroupId))],
                page.Total,
                page.Page,
                page.PageSize));
    }
}

internal static class Names
{
    public static TextDto Of(IReadOnlyDictionary<Guid, UserInfo> people, Guid id)
    {
        var person = people.GetValueOrDefault(id);
        return new TextDto(person?.NameAr ?? "", person?.NameEn ?? "");
    }
}

public sealed record StartTopicCommand(Guid CohortId, string Title, string Body, Guid? GroupId = null) : ICommand<Guid>;

internal sealed class StartTopicValidator : AbstractValidator<StartTopicCommand>
{
    public StartTopicValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(ForumTopic.TitleMaxLength);
        RuleFor(c => c.Body).NotEmpty().MaximumLength(ForumTopic.BodyMaxLength);
    }
}

internal sealed class StartTopicHandler(ForumAccess access, IForumRepository forums, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<StartTopicCommand, Guid>
{
    public async Task<Result<Guid>> Handle(StartTopicCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        if (request.GroupId is { } group && !member.Value.IsModerator && group != member.Value.GroupId)
        {
            return CommunityErrors.NotAMember;
        }

        var topic = ForumTopic.Start(request.CohortId, user.Id!.Value, request.Title, request.Body, clock.GetUtcNow(), request.GroupId);
        if (!topic.IsSuccess)
        {
            return topic.Error!;
        }

        forums.Add(topic.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return topic.Value.Id.Value;
    }
}

public sealed record PostView(Guid Id, Guid AuthorId, TextDto AuthorName, string? Body, DateTimeOffset CreatedAt, bool IsRemoved, bool CanRemove);

public sealed record TopicDetailResponse(
    Guid Id,
    string Title,
    string Body,
    Guid AuthorId,
    TextDto AuthorName,
    DateTimeOffset CreatedAt,
    bool IsPinned,
    bool IsLocked,
    bool CanModerate,
    bool CanRemove,
    IReadOnlyList<PostView> Posts);

public sealed record GetTopicQuery(Guid CohortId, Guid TopicId) : IQuery<TopicDetailResponse>;

internal sealed class GetTopicHandler(ForumAccess access, IForumRepository forums, IUserDirectory directory, ICurrentUser user)
    : IRequestHandler<GetTopicQuery, TopicDetailResponse>
{
    public async Task<Result<TopicDetailResponse>> Handle(GetTopicQuery request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var topic = await forums.FindTopicAsync(new TopicId(request.TopicId), cancellationToken);
        if (topic is null || topic.CohortId != request.CohortId || topic.RemovedAt is not null || !member.Value.Sees(topic))
        {
            return CommunityErrors.TopicNotFound;
        }

        var posts = await forums.ListPostsAsync(topic.Id, cancellationToken);
        var names = await directory.GetAsync([.. posts.Select(p => p.AuthorId).Append(topic.AuthorId).Distinct()], cancellationToken);
        var moderator = member.Value.IsModerator;

        return new TopicDetailResponse(
            topic.Id.Value,
            topic.Title,
            topic.Body,
            topic.AuthorId,
            Names.Of(names, topic.AuthorId),
            topic.CreatedAt,
            topic.IsPinned,
            topic.IsLocked,
            moderator,
            moderator || topic.AuthorId == user.Id,
            [.. posts.Select(p =>
            {
                var removed = p.RemovedAt is not null;

                // Removed text never leaves the server, even for moderators.
                return new PostView(p.Id.Value, p.AuthorId, Names.Of(names, p.AuthorId), removed ? null : p.Body, p.CreatedAt, removed, !removed && (moderator || p.AuthorId == user.Id));
            })]);
    }
}

public sealed record ReplyToTopicCommand(Guid CohortId, Guid TopicId, string Body) : ICommand<Guid>;

internal sealed class ReplyToTopicValidator : AbstractValidator<ReplyToTopicCommand>
{
    public ReplyToTopicValidator() => RuleFor(c => c.Body).NotEmpty().MaximumLength(ForumTopic.BodyMaxLength);
}

internal sealed class ReplyToTopicHandler(ForumAccess access, IForumRepository forums, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<ReplyToTopicCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ReplyToTopicCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var topic = await forums.FindTopicAsync(new TopicId(request.TopicId), cancellationToken);
        if (topic is null || topic.CohortId != request.CohortId || !member.Value.Sees(topic))
        {
            return CommunityErrors.TopicNotFound;
        }

        var post = topic.Reply(user.Id!.Value, request.Body, member.Value.IsModerator, clock.GetUtcNow());
        if (!post.IsSuccess)
        {
            return post.Error!;
        }

        forums.Add(post.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return post.Value.Id.Value;
    }
}

public sealed record ModerateTopicCommand(Guid CohortId, Guid TopicId, bool? Pinned, bool? Locked) : ICommand<Unit>;

internal sealed class ModerateTopicHandler(ForumAccess access, IForumRepository forums, ICommunityUnitOfWork unitOfWork)
    : IRequestHandler<ModerateTopicCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ModerateTopicCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        if (!member.Value.IsModerator)
        {
            return CommunityErrors.NotAllowed;
        }

        var topic = await forums.FindTopicAsync(new TopicId(request.TopicId), cancellationToken);
        if (topic is null || topic.CohortId != request.CohortId || !member.Value.Sees(topic))
        {
            return CommunityErrors.TopicNotFound;
        }

        if (request.Pinned is { } pinned)
        {
            topic.SetPinned(pinned);
        }

        if (request.Locked is { } locked)
        {
            topic.SetLocked(locked);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RemoveTopicCommand(Guid CohortId, Guid TopicId) : ICommand<Unit>;

internal sealed class RemoveTopicHandler(ForumAccess access, IForumRepository forums, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<RemoveTopicCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemoveTopicCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var topic = await forums.FindTopicAsync(new TopicId(request.TopicId), cancellationToken);
        if (topic is null || topic.CohortId != request.CohortId || !member.Value.Sees(topic))
        {
            return CommunityErrors.TopicNotFound;
        }

        if (!member.Value.IsModerator && topic.AuthorId != user.Id)
        {
            return CommunityErrors.NotAllowed;
        }

        topic.Remove(user.Id!.Value, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record RemovePostCommand(Guid CohortId, Guid TopicId, Guid PostId) : ICommand<Unit>;

internal sealed class RemovePostHandler(ForumAccess access, IForumRepository forums, ICurrentUser user, ICommunityUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<RemovePostCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RemovePostCommand request, CancellationToken cancellationToken)
    {
        var member = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!member.IsSuccess)
        {
            return member.Error!;
        }

        var topic = await forums.FindTopicAsync(new TopicId(request.TopicId), cancellationToken);
        var post = await forums.FindPostAsync(new PostId(request.PostId), cancellationToken);

        if (topic is null || topic.CohortId != request.CohortId || !member.Value.Sees(topic) || post is null || post.TopicId != topic.Id)
        {
            return CommunityErrors.PostNotFound;
        }

        if (!member.Value.IsModerator && post.AuthorId != user.Id)
        {
            return CommunityErrors.NotAllowed;
        }

        if (post.RemovedAt is null)
        {
            post.Remove(user.Id!.Value, clock.GetUtcNow());
            topic.PostRemoved();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
