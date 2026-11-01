using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Community.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Community.Application;

public sealed record ConversationRow(Conversation Conversation, string LastBody, int Unread);

public interface IConversationRepository
{
    Task<Conversation?> FindAsync(Guid first, Guid second, CancellationToken cancellationToken);

    Task<ChatMessage?> FindMessageAsync(MessageId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationRow>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(ConversationId id, int take, CancellationToken cancellationToken);

    Task MarkReadAsync(ConversationId id, Guid readerId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Conversation conversation);

    void Add(ChatMessage message);
}

/// <summary>Trainees reach their instructors, classmates and staff. Coordinators and admins can reach anyone.</summary>
public sealed class MessagingPolicy(ICohortDirectory cohorts, IUserDirectory directory, ICurrentUser user)
{
    private static bool IsStaffRole(string? role) => role is LmsRoles.Admin or LmsRoles.Coordinator;

    public async Task<Result> CanMessageAsync(Guid recipientId, CancellationToken cancellationToken)
    {
        var senderId = user.Id!.Value;

        if (senderId == recipientId)
        {
            return CommunityErrors.CannotMessageSelf;
        }

        var people = await directory.GetAsync([recipientId], cancellationToken);
        if (!people.TryGetValue(recipientId, out var recipient))
        {
            return CommunityErrors.UserNotFound;
        }

        if (IsStaffRole(user.Role) || IsStaffRole(recipient.Role))
        {
            return Result.Success();
        }

        var contacts = await cohorts.GetContactIdsAsync(senderId, cancellationToken);
        return contacts.Contains(recipientId) ? Result.Success() : CommunityErrors.CannotMessageUser;
    }
}

public sealed record ContactResponse(Guid Id, TextDto Name, string Role);

public sealed record ListContactsQuery(string? Search) : IQuery<IReadOnlyList<ContactResponse>>;

internal sealed class ListContactsValidator : AbstractValidator<ListContactsQuery>
{
    public ListContactsValidator() => RuleFor(q => q.Search).MaximumLength(100);
}

internal sealed class ListContactsHandler(ICohortDirectory cohorts, IUserDirectory directory, ICurrentUser user)
    : IRequestHandler<ListContactsQuery, IReadOnlyList<ContactResponse>>
{
    private const int MaxResults = 30;

    public async Task<Result<IReadOnlyList<ContactResponse>>> Handle(ListContactsQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        IReadOnlyCollection<UserInfo> people;

        if (user.Role is LmsRoles.Admin or LmsRoles.Coordinator)
        {
            people = await directory.SearchAsync(request.Search, MaxResults, cancellationToken);
        }
        else
        {
            var ids = await cohorts.GetContactIdsAsync(me, cancellationToken);
            var found = await directory.GetAsync(ids, cancellationToken);
            var term = request.Search?.Trim();

            people = [.. found.Values.Where(p => string.IsNullOrEmpty(term)
                || p.NameAr.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.NameEn.Contains(term, StringComparison.OrdinalIgnoreCase))];
        }

        IReadOnlyList<ContactResponse> items = [.. people.Where(p => p.Id != me).Take(MaxResults).Select(p => new ContactResponse(p.Id, new TextDto(p.NameAr, p.NameEn), p.Role))];
        return Result<IReadOnlyList<ContactResponse>>.Success(items);
    }
}

public sealed record ConversationSummary(Guid UserId, TextDto Name, string Role, string LastMessage, DateTimeOffset LastMessageAt, int Unread);

public sealed record ListConversationsQuery : IQuery<IReadOnlyList<ConversationSummary>>;

internal sealed class ListConversationsHandler(IConversationRepository conversations, IUserDirectory directory, ICurrentUser user)
    : IRequestHandler<ListConversationsQuery, IReadOnlyList<ConversationSummary>>
{
    private const int PreviewLength = 90;

    public async Task<Result<IReadOnlyList<ConversationSummary>>> Handle(ListConversationsQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        var rows = await conversations.ListForUserAsync(me, cancellationToken);
        var people = await directory.GetAsync([.. rows.Select(r => r.Conversation.OtherThan(me))], cancellationToken);

        IReadOnlyList<ConversationSummary> items = [.. rows.Select(r =>
        {
            var other = r.Conversation.OtherThan(me);
            var preview = r.LastBody.Length > PreviewLength ? r.LastBody[..PreviewLength] : r.LastBody;
            return new ConversationSummary(other, Names.Of(people, other), people.GetValueOrDefault(other)?.Role ?? "", preview, r.Conversation.LastMessageAt, r.Unread);
        })];

        return Result<IReadOnlyList<ConversationSummary>>.Success(items);
    }
}

public sealed record MessageView(Guid Id, bool IsMine, string Body, DateTimeOffset SentAt, bool IsRead);

public sealed record ThreadResponse(Guid UserId, TextDto Name, string Role, IReadOnlyList<MessageView> Messages);

public sealed record GetThreadQuery(Guid OtherUserId) : IQuery<ThreadResponse>;

internal sealed class GetThreadHandler(MessagingPolicy policy, IConversationRepository conversations, IUserDirectory directory, ICurrentUser user)
    : IRequestHandler<GetThreadQuery, ThreadResponse>
{
    private const int History = 200;

    public async Task<Result<ThreadResponse>> Handle(GetThreadQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        var people = await directory.GetAsync([request.OtherUserId], cancellationToken);

        if (!people.TryGetValue(request.OtherUserId, out var other))
        {
            return CommunityErrors.UserNotFound;
        }

        var (first, second) = Conversation.Order(me, request.OtherUserId);
        var conversation = await conversations.FindAsync(first, second, cancellationToken);

        // A person someone may not message stays invisible, unless they already share a thread.
        if (conversation is null)
        {
            var allowed = await policy.CanMessageAsync(request.OtherUserId, cancellationToken);
            if (!allowed.IsSuccess)
            {
                return allowed.Error!;
            }
        }

        var messages = conversation is null ? [] : await conversations.ListMessagesAsync(conversation.Id, History, cancellationToken);

        return new ThreadResponse(
            other.Id,
            new TextDto(other.NameAr, other.NameEn),
            other.Role,
            [.. messages.Select(m => new MessageView(m.Id.Value, m.SenderId == me, m.Body, m.SentAt, m.ReadAt is not null))]);
    }
}

public sealed record MarkThreadReadCommand(Guid OtherUserId) : ICommand<Unit>, INotAudited;

internal sealed class MarkThreadReadHandler(IConversationRepository conversations, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<MarkThreadReadCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MarkThreadReadCommand request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        var (first, second) = Conversation.Order(me, request.OtherUserId);
        var conversation = await conversations.FindAsync(first, second, cancellationToken);

        if (conversation is not null)
        {
            await conversations.MarkReadAsync(conversation.Id, me, clock.GetUtcNow(), cancellationToken);
        }

        return default(Unit);
    }
}

public sealed record UnreadMessagesResponse(int Count);

public sealed record GetUnreadMessagesQuery : IQuery<UnreadMessagesResponse>;

internal sealed class GetUnreadMessagesHandler(IConversationRepository conversations, ICurrentUser user)
    : IRequestHandler<GetUnreadMessagesQuery, UnreadMessagesResponse>
{
    public async Task<Result<UnreadMessagesResponse>> Handle(GetUnreadMessagesQuery request, CancellationToken cancellationToken) =>
        new UnreadMessagesResponse(await conversations.CountUnreadAsync(user.Id!.Value, cancellationToken));
}

public sealed record SendMessageCommand(Guid RecipientId, string Body) : ICommand<Guid>;

internal sealed class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator() => RuleFor(c => c.Body).NotEmpty().MaximumLength(ChatMessage.BodyMaxLength);
}

internal sealed class SendMessageHandler(
    MessagingPolicy policy,
    IConversationRepository conversations,
    ICurrentUser user,
    ICommunityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SendMessageCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var allowed = await policy.CanMessageAsync(request.RecipientId, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        var me = user.Id!.Value;
        var now = clock.GetUtcNow();
        var (first, second) = Conversation.Order(me, request.RecipientId);
        var conversation = await conversations.FindAsync(first, second, cancellationToken);

        if (conversation is null)
        {
            var created = Conversation.Between(me, request.RecipientId, now);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            conversation = created.Value;
            conversations.Add(conversation);
        }

        var message = conversation.Send(me, request.Body, now);
        if (!message.IsSuccess)
        {
            return message.Error!;
        }

        conversations.Add(message.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return message.Value.Id.Value;
    }
}
