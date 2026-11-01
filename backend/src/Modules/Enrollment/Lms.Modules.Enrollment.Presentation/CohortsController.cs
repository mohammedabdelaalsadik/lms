using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Enrollment.Application;
using Lms.Modules.Enrollment.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Enrollment.Presentation;

public sealed record RejectRequest(string Reason);

public sealed record GroupRequest(string Name);

public sealed record PlaceRequest(Guid? GroupId);

public sealed record SplitRequest(int Count);

[Authorize]
[Route("api/v1/cohorts")]
public sealed class CohortsController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] CohortStatus? status,
        [FromQuery] CohortPeriod? period,
        [FromQuery] string? branch,
        [FromQuery] Guid? instructorId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListCohortsQuery(search, status, page, pageSize, period, branch, instructorId), cancellationToken));

    [HttpGet("branches")]
    public async Task<IActionResult> Branches(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListCohortBranchesQuery(), cancellationToken));

    [HttpGet("{id:guid}/groups")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Groups(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetGroupBoardQuery(id), cancellationToken));

    [HttpPost("{id:guid}/groups")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> AddGroup(Guid id, GroupRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveGroupCommand(id, null, body.Name), cancellationToken));

    [HttpPut("{id:guid}/groups/{groupId:guid}")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> RenameGroup(Guid id, Guid groupId, GroupRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveGroupCommand(id, groupId, body.Name), cancellationToken));

    [HttpDelete("{id:guid}/groups/{groupId:guid}")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> DeleteGroup(Guid id, Guid groupId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new DeleteGroupCommand(id, groupId), cancellationToken));

    [HttpPut("{id:guid}/groups/members/{traineeId:guid}")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Place(Guid id, Guid traineeId, PlaceRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new PlaceTraineeCommand(id, traineeId, body.GroupId), cancellationToken));

    [HttpPost("{id:guid}/groups/split")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Split(Guid id, SplitRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SplitIntoGroupsCommand(id, body.Count), cancellationToken));

    [HttpGet("{id:guid}/roster")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Roster(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCohortRosterQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(CreateCohortCommand command, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(command, cancellationToken));

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new CloseCohortCommand(id), cancellationToken));

    [HttpPost("{id:guid}/enroll")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Enroll(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new EnrollInCohortCommand(id), cancellationToken));

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new WithdrawFromCohortCommand(id), cancellationToken));

    [HttpPost("{id:guid}/enrollments/{enrollmentId:guid}/approve")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Approve(Guid id, Guid enrollmentId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ApproveEnrollmentCommand(id, enrollmentId), cancellationToken));

    [HttpPost("{id:guid}/enrollments/{enrollmentId:guid}/reject")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Reject(Guid id, Guid enrollmentId, RejectRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RejectEnrollmentCommand(id, enrollmentId, body.Reason), cancellationToken));
}

[Authorize(Roles = LmsRoles.Trainee)]
[Route("api/v1/my/enrollments")]
public sealed class MyEnrollmentsController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyEnrollmentsQuery(), cancellationToken));
}
