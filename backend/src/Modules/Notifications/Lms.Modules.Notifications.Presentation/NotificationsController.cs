using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Notifications.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Notifications.Presentation;

[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] bool onlyUnread = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListMyNotificationsQuery(onlyUnread, page, pageSize), cancellationToken));

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetUnreadCountQuery(), cancellationToken));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new MarkNotificationReadCommand(id), cancellationToken));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new MarkAllNotificationsReadCommand(), cancellationToken));
}
