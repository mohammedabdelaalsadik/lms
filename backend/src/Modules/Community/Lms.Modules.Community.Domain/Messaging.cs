using Lms.SharedKernel;

namespace Lms.Modules.Community.Domain;

public readonly record struct ConversationId(Guid Value);

public readonly record struct MessageId(Guid Value);

public sealed record MessageSent(Guid MessageId, Guid SenderId, Guid RecipientId) : IDomainEvent;

/// <summary>A private thread between two people. The pair is stored in a fixed order, so there is only ever one.</summary>
public sealed class Conversation : AggregateRoot<ConversationId>
{
    private Conversation()
    {
    }

    public Guid UserA { get; private set; }
    public Guid UserB { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }

    public static (Guid First, Guid Second) Order(Guid one, Guid two) => one.CompareTo(two) < 0 ? (one, two) : (two, one);

    public static Result<Conversation> Between(Guid one, Guid two, DateTimeOffset now)
    {
        if (one == two)
        {
            return CommunityErrors.CannotMessageSelf;
        }

        var (first, second) = Order(one, two);

        return new Conversation
        {
            Id = new ConversationId(Guid.CreateVersion7()),
            UserA = first,
            UserB = second,
            LastMessageAt = now,
        };
    }

    public bool Includes(Guid userId) => UserA == userId || UserB == userId;

    public Guid OtherThan(Guid userId) => UserA == userId ? UserB : UserA;

    public Result<ChatMessage> Send(Guid senderId, string? body, DateTimeOffset now)
    {
        if (!Includes(senderId))
        {
            return CommunityErrors.NotAllowed;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return CommunityErrors.EmptyText;
        }

        var message = ChatMessage.Create(Id, senderId, body.Trim(), now);
        LastMessageAt = now;
        Raise(new MessageSent(message.Id.Value, senderId, OtherThan(senderId)));
        return message;
    }
}

public sealed class ChatMessage
{
    public const int BodyMaxLength = 4000;

    private ChatMessage()
    {
    }

    public MessageId Id { get; private set; }
    public ConversationId ConversationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Body { get; private set; } = null!;
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    internal static ChatMessage Create(ConversationId conversationId, Guid senderId, string body, DateTimeOffset now) => new()
    {
        Id = new MessageId(Guid.CreateVersion7()),
        ConversationId = conversationId,
        SenderId = senderId,
        Body = body,
        SentAt = now,
    };
}
