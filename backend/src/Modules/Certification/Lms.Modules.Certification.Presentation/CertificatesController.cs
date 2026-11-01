using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lms.Modules.Certification.Presentation;

public sealed record RevokeRequest(string Reason);

[Authorize]
[Route("api/v1/certificates")]
public sealed class CertificatesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    public const string VerifyRateLimit = "verify";

    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting(VerifyRateLimit)]
    public async Task<IActionResult> Verify(string code, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new VerifyCertificateQuery(code), cancellationToken));

    [HttpGet("my")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyCertificatesQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Roles = LmsRoles.CertificateViewers)]
    public async Task<IActionResult> List(
        [FromQuery] CertificateStatus? status,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListCertificatesQuery(status, search, page, pageSize, from, to), cancellationToken));

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new DownloadCertificateQuery(id), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "application/pdf", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> Revoke(Guid id, RevokeRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RevokeCertificateCommand(id, body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reissue")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> Reissue(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ReissueCertificateCommand(id), cancellationToken));
}

public sealed record CompetencyRequest(string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CourseIds);

public sealed record CompetencyUpdateRequest(string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CourseIds);

[Authorize]
[Route("api/v1/competencies")]
public sealed class CompetenciesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    [Authorize(Roles = LmsRoles.CertificateViewers)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListCompetenciesQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(CompetencyRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreateCompetencyCommand(body.Code, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.CourseIds), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Update(Guid id, CompetencyUpdateRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new UpdateCompetencyCommand(id, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.CourseIds), cancellationToken));

    [HttpPost("{id:guid}/retire")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RetireCompetencyCommand(id), cancellationToken));

    [HttpPut("{id:guid}/sign-offs/{traineeId:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> SignOff(Guid id, Guid traineeId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SetSignOffCommand(id, traineeId, true), cancellationToken));

    [HttpDelete("{id:guid}/sign-offs/{traineeId:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> ClearSignOff(Guid id, Guid traineeId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SetSignOffCommand(id, traineeId, false), cancellationToken));
}

public sealed record PlanRequest(string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, IReadOnlyList<Guid> CompetencyIds);

public sealed record AssignPlanRequest(Guid CohortId, DateOnly? DueOn);

public sealed record AssignPlanTraineeRequest(DateOnly? DueOn);

[Authorize]
[Route("api/v1/learning-plans")]
public sealed class LearningPlansController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("my")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyPlansQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Roles = LmsRoles.CertificateViewers)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListPlansQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = LmsRoles.CertificateViewers)]
    public async Task<IActionResult> Progress(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetPlanProgressQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(PlanRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SavePlanCommand(null, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.CompetencyIds), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Update(Guid id, PlanRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SavePlanCommand(id, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.CompetencyIds), cancellationToken));

    [HttpPost("{id:guid}/retire")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RetirePlanCommand(id), cancellationToken));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Assign(Guid id, AssignPlanRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new AssignPlanToCohortCommand(id, body.CohortId, body.DueOn), cancellationToken));

    [HttpPost("{id:guid}/trainees/{traineeId:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> AssignOne(Guid id, Guid traineeId, AssignPlanTraineeRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new AssignPlanToTraineeCommand(id, traineeId, body.DueOn), cancellationToken));

    [HttpDelete("{id:guid}/trainees/{traineeId:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Unassign(Guid id, Guid traineeId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new UnassignPlanCommand(id, traineeId), cancellationToken));
}

public sealed record BadgeRequest(Guid CourseId, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, string Icon);

[Authorize]
[Route("api/v1/badges")]
public sealed class BadgesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("my")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyBadgesQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Roles = LmsRoles.CertificateViewers)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListBadgesQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(BadgeRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreateBadgeCommand(body.CourseId, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.Icon), cancellationToken));

    [HttpPost("{id:guid}/retire")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RetireBadgeCommand(id), cancellationToken));
}
