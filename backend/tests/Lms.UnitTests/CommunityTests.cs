using Lms.Modules.Community.Domain;

namespace Lms.UnitTests;

public class ForumTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static ForumTopic New() => ForumTopic.Start(Guid.NewGuid(), Author, " سؤال ", " نص ", Now).Value;

    [Theory]
    [InlineData(" ", "body")]
    [InlineData("title", " ")]
    [InlineData(null, "body")]
    public void Topic_needs_a_title_and_text(string? title, string? body) =>
        Assert.Equal(CommunityErrors.EmptyText, ForumTopic.Start(Guid.NewGuid(), Author, title, body, Now).Error);

    [Fact]
    public void Reply_counts_moves_the_topic_up_and_tells_the_author()
    {
        var topic = New();

        var post = topic.Reply(Other, " شكرا ", false, Now.AddHours(1));

        Assert.Equal("شكرا", post.Value.Body);
        Assert.Equal((1, Now.AddHours(1)), (topic.ReplyCount, topic.LastActivityAt));
        Assert.IsType<TopicReplied>(Assert.Single(topic.DomainEvents));
    }

    [Fact]
    public void Replying_to_your_own_topic_notifies_nobody()
    {
        var topic = New();

        topic.Reply(Author, "تحديث", false, Now);

        Assert.Empty(topic.DomainEvents);
    }

    [Fact]
    public void Locked_topic_refuses_trainees_but_not_moderators()
    {
        var topic = New();
        topic.SetLocked(true);

        Assert.Equal(CommunityErrors.TopicLocked, topic.Reply(Other, "x", false, Now).Error);
        Assert.True(topic.Reply(Other, "x", true, Now).IsSuccess);
    }

    [Fact]
    public void Removed_topic_takes_no_replies_and_keeps_who_removed_it()
    {
        var topic = New();

        topic.Remove(Other, Now);

        Assert.Equal(CommunityErrors.TopicNotFound, topic.Reply(Author, "x", true, Now).Error);
        Assert.Equal(Other, topic.RemovedBy);
    }

    [Fact]
    public void Empty_reply_changes_nothing()
    {
        var topic = New();

        Assert.Equal(CommunityErrors.EmptyText, topic.Reply(Other, "  ", false, Now).Error);
        Assert.Equal(0, topic.ReplyCount);
    }
}

public class ConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid One = Guid.NewGuid();
    private static readonly Guid Two = Guid.NewGuid();

    [Fact]
    public void Pair_order_is_the_same_from_either_side() =>
        Assert.Equal(Conversation.Order(One, Two), Conversation.Order(Two, One));

    [Fact]
    public void Cannot_message_yourself() =>
        Assert.Equal(CommunityErrors.CannotMessageSelf, Conversation.Between(One, One, Now).Error);

    [Fact]
    public void Message_names_the_other_person_as_recipient()
    {
        var conversation = Conversation.Between(One, Two, Now).Value;

        conversation.Send(One, "مرحبا", Now.AddMinutes(1));

        var sent = Assert.IsType<MessageSent>(Assert.Single(conversation.DomainEvents));
        Assert.Equal((One, Two), (sent.SenderId, sent.RecipientId));
        Assert.Equal(Now.AddMinutes(1), conversation.LastMessageAt);
    }

    [Fact]
    public void Outsider_cannot_post_into_a_conversation() =>
        Assert.Equal(CommunityErrors.NotAllowed, Conversation.Between(One, Two, Now).Value.Send(Guid.NewGuid(), "x", Now).Error);

    [Fact]
    public void Event_carries_no_message_text()
    {
        var conversation = Conversation.Between(One, Two, Now).Value;

        conversation.Send(One, "نص خاص", Now);

        Assert.DoesNotContain("نص خاص", System.Text.Json.JsonSerializer.Serialize(conversation.DomainEvents.Single(), conversation.DomainEvents.Single().GetType()));
    }
}
