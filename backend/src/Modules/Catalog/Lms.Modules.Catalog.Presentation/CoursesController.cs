using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Catalog.Application;
using Lms.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Catalog.Presentation;

public sealed record UpdateCourseRequest(
    string TitleAr,
    string TitleEn,
    string DescriptionAr,
    string DescriptionEn,
    int DurationHours);

public sealed record MoveCourseRequest(Guid? CategoryId);

public sealed record CategoryRequest(string NameAr, string NameEn, Guid? ParentId);

[Authorize]
[Route("api/v1/course-categories")]
public sealed class CourseCategoriesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListCategoriesQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(CategoryRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveCategoryCommand(null, body.NameAr, body.NameEn, body.ParentId), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Update(Guid id, CategoryRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveCategoryCommand(id, body.NameAr, body.NameEn, body.ParentId), cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new DeleteCategoryCommand(id), cancellationToken));
}

[Authorize]
[Route("api/v1/courses")]
public sealed class CoursesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] CourseStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null) =>
        Respond(await Dispatcher.Send(new ListCoursesQuery(status, search, page, pageSize, categoryId), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCourseQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Create(CreateCourseCommand command, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(command, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Update(Guid id, UpdateCourseRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(
            new UpdateCourseCommand(id, body.TitleAr, body.TitleEn, body.DescriptionAr, body.DescriptionEn, body.DurationHours),
            cancellationToken));

    [HttpPut("{id:guid}/category")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Move(Guid id, MoveCourseRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new MoveCourseCommand(id, body.CategoryId), cancellationToken));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new PublishCourseCommand(id), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ArchiveCourseCommand(id), cancellationToken));
}
