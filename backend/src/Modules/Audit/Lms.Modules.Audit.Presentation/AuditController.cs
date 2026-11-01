using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Audit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Audit.Presentation;

[Authorize(Roles = LmsRoles.UserViewers)]
[Route("api/v1/audit")]
public sealed class AuditController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    /// <summary>Read only by design. No endpoint changes or removes an entry.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? actorId,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListAuditEntriesQuery(actorId, search, from, to, page, pageSize), cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? search, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportAuditEntriesQuery(search, from, to), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName) : Failure(result.Error!);
    }
}
