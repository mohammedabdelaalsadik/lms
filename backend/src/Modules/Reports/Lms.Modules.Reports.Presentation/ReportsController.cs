using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Reports.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Reports.Presentation;

[Authorize]
[Route("api/v1/search")]
public sealed class SearchController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GlobalSearchQuery(q), cancellationToken));
}

[Authorize(Roles = LmsRoles.CohortViewers + "," + LmsRoles.Auditor)]
[Route("api/v1/reports")]
public sealed class ReportsController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetDashboardQuery(), cancellationToken));

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetOverviewQuery(), cancellationToken));

    [HttpGet("overview/export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportOverviewQuery(), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName) : Failure(result.Error!);
    }
}
