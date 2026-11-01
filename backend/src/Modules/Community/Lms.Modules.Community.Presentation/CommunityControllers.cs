using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Community.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Community.Presentation;

public sealed record TopicRequest(string Title, string Body, Guid? GroupId = null);

public sealed record BodyRequest(string Body);

public sealed record ModerateRequest(bool? Pinned, bool? Locked);

public sealed record WikiPageRequest(string Title, string Body);

public sealed record WikiEditRequest(string Title, string Body, int Version);

[Authorize]
[Route("api/v1/wikis/{cohortId:guid}/pages")]
public sealed class WikiController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListWikiQuery(cohortId), cancellationToken));

    [HttpGet("{pageId:guid}")]
    public async Task<IActionResult> Get(Guid cohortId, Guid pageId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetWikiPageQuery(cohortId, pageId), cancellationToken));

    [HttpGet("{pageId:guid}/revisions/{version:int}")]
    public async Task<IActionResult> Revision(Guid cohortId, Guid pageId, int version, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetWikiRevisionQuery(cohortId, pageId, version), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(Guid cohortId, WikiPageRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreateWikiPageCommand(cohortId, body.Title, body.Body), cancellationToken));

    [HttpPut("{pageId:guid}")]
    public async Task<IActionResult> Edit(Guid cohortId, Guid pageId, WikiEditRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new EditWikiPageCommand(cohortId, pageId, body.Title, body.Body, body.Version), cancellationToken));

    [HttpPost("{pageId:guid}/revisions/{version:int}/restore")]
    public async Task<IActionResult> Restore(Guid cohortId, Guid pageId, int version, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RestoreWikiRevisionCommand(cohortId, pageId, version), cancellationToken));

    [HttpDelete("{pageId:guid}")]
    public async Task<IActionResult> Remove(Guid cohortId, Guid pageId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RemoveWikiPageCommand(cohortId, pageId), cancellationToken));
}

[Authorize]
[Route("api/v1/forums")]
public sealed class ForumsController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> Forums(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListForumsQuery(), cancellationToken));

    [HttpGet("{cohortId:guid}/topics")]
    public async Task<IActionResult> List(Guid cohortId, [FromQuery] string? search, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListTopicsQuery(cohortId, search, page, pageSize), cancellationToken));

    [HttpPost("{cohortId:guid}/topics")]
    public async Task<IActionResult> Start(Guid cohortId, TopicRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new StartTopicCommand(cohortId, body.Title, body.Body, body.GroupId), cancellationToken));

    [HttpGet("{cohortId:guid}/topics/{topicId:guid}")]
    public async Task<IActionResult> Get(Guid cohortId, Guid topicId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetTopicQuery(cohortId, topicId), cancellationToken));

    [HttpPost("{cohortId:guid}/topics/{topicId:guid}/posts")]
    public async Task<IActionResult> Reply(Guid cohortId, Guid topicId, BodyRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ReplyToTopicCommand(cohortId, topicId, body.Body), cancellationToken));

    [HttpPut("{cohortId:guid}/topics/{topicId:guid}/moderation")]
    public async Task<IActionResult> Moderate(Guid cohortId, Guid topicId, ModerateRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ModerateTopicCommand(cohortId, topicId, body.Pinned, body.Locked), cancellationToken));

    [HttpDelete("{cohortId:guid}/topics/{topicId:guid}")]
    public async Task<IActionResult> Remove(Guid cohortId, Guid topicId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RemoveTopicCommand(cohortId, topicId), cancellationToken));

    [HttpDelete("{cohortId:guid}/topics/{topicId:guid}/posts/{postId:guid}")]
    public async Task<IActionResult> RemovePost(Guid cohortId, Guid topicId, Guid postId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RemovePostCommand(cohortId, topicId, postId), cancellationToken));
}

[Authorize]
[Route("api/v1/messages")]
public sealed class MessagesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> Conversations(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListConversationsQuery(), cancellationToken));

    [HttpGet("contacts")]
    public async Task<IActionResult> Contacts([FromQuery] string? search, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListContactsQuery(search), cancellationToken));

    [HttpGet("unread-count")]
    public async Task<IActionResult> Unread(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetUnreadMessagesQuery(), cancellationToken));

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Thread(Guid userId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetThreadQuery(userId), cancellationToken));

    [HttpPost("{userId:guid}")]
    public async Task<IActionResult> Send(Guid userId, BodyRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SendMessageCommand(userId, body.Body), cancellationToken));

    [HttpPost("{userId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid userId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new MarkThreadReadCommand(userId), cancellationToken));
}
