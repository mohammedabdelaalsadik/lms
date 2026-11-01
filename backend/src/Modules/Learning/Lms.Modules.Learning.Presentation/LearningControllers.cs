using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Modules.Learning.Presentation;

public sealed record SettingsRequest(bool IsSequential, int MinAttendancePercent, int PlannedSessions, int PassMarkPercent);

public sealed record SectionRequest(string TitleAr, string TitleEn);

public sealed record MoveRequest(MoveDirection Direction);

public sealed record ActivityRequest(
    ActivityKind Kind,
    string TitleAr,
    string TitleEn,
    bool IsRequired,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    FormDefinition? Form = null,
    BookDefinition? Book = null,
    LessonDefinition? Lesson = null);

public sealed record GlossaryRequest(string TermAr, string TermEn, string DefinitionAr, string DefinitionEn);

public sealed record SubmitFormRequest(IReadOnlyList<FormAnswer> Answers);

public sealed record SaveAttendanceRequest(DateOnly Date, IReadOnlyList<AttendanceEntry> Entries);

public sealed record LiveClassRequest(string TitleAr, string TitleEn, DateTimeOffset StartsAt, int DurationMinutes);

[Authorize]
[Route("api/v1/learning/cohorts/{cohortId:guid}/live")]
public sealed class LiveClassesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListLiveClassesQuery(cohortId), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Schedule(Guid cohortId, LiveClassRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ScheduleLiveClassCommand(cohortId, body.TitleAr, body.TitleEn, body.StartsAt, body.DurationMinutes), cancellationToken));

    [HttpPost("{id:guid}/join")]
    public async Task<IActionResult> Join(Guid cohortId, Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Respond(await Dispatcher.Send(new JoinLiveClassCommand(cohortId, id), cancellationToken));
    }

    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid cohortId, Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new EndLiveClassCommand(cohortId, id), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid cohortId, Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new CancelLiveClassCommand(cohortId, id), cancellationToken));
}

[Authorize]
[Route("api/v1/my-files")]
public sealed class MyFilesController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyFilesQuery(), cancellationToken));

    [HttpPost]
    [RequestSizeLimit(FileRules.MaxSize + (1024 * 1024))]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return Respond(await Dispatcher.Send(new UploadMyFileCommand(file.FileName, file.Length, content), cancellationToken));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new DownloadMyFileQuery(id), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Failure(result.Error!);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RemoveMyFileCommand(id), cancellationToken));
}

[Authorize]
[Route("api/v1/learning/courses/{courseId:guid}/glossary")]
public sealed class GlossaryController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid courseId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetGlossaryQuery(courseId), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Add(Guid courseId, GlossaryRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveGlossaryEntryCommand(courseId, null, body.TermAr, body.TermEn, body.DefinitionAr, body.DefinitionEn), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid courseId, Guid id, GlossaryRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveGlossaryEntryCommand(courseId, id, body.TermAr, body.TermEn, body.DefinitionAr, body.DefinitionEn), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid courseId, Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ApproveGlossaryEntryCommand(courseId, id), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid courseId, Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RemoveGlossaryEntryCommand(courseId, id), cancellationToken));
}

[Authorize(Roles = LmsRoles.CatalogManagers)]
[Route("api/v1/learning/courses")]
public sealed class CourseBackupController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("{courseId:guid}/backup")]
    public async Task<IActionResult> Backup(Guid courseId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportCourseQuery(courseId), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "application/zip", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpPost("restore")]
    [RequestSizeLimit(BackupFormat.MaxBackupSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupFormat.MaxBackupSize)]
    public async Task<IActionResult> Restore([FromForm] string code, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return Respond(await Dispatcher.Send(new RestoreCourseCommand(code, content), cancellationToken));
    }
}

[Authorize(Roles = LmsRoles.CohortViewers)]
[Route("api/v1/learning/courses/{courseId:guid}/content")]
public sealed class CourseContentController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    private const long UploadLimit = FileRules.MaxSize + (1024 * 1024);

    [HttpGet]
    public async Task<IActionResult> Get(Guid courseId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCourseContentQuery(courseId), cancellationToken));

    [HttpPut("settings")]
    public async Task<IActionResult> Settings(Guid courseId, SettingsRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new UpdateContentSettingsCommand(courseId, body.IsSequential, body.MinAttendancePercent, body.PlannedSessions, body.PassMarkPercent), cancellationToken));

    [HttpPost("sections")]
    public async Task<IActionResult> AddSection(Guid courseId, SectionRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveSectionCommand(courseId, null, body.TitleAr, body.TitleEn), cancellationToken));

    [HttpPut("sections/{sectionId:guid}")]
    public async Task<IActionResult> RenameSection(Guid courseId, Guid sectionId, SectionRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new SaveSectionCommand(courseId, sectionId, body.TitleAr, body.TitleEn), cancellationToken));

    [HttpDelete("sections/{sectionId:guid}")]
    public async Task<IActionResult> RemoveSection(Guid courseId, Guid sectionId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new RemoveSectionCommand(courseId, sectionId), cancellationToken));

    [HttpPost("sections/{sectionId:guid}/move")]
    public async Task<IActionResult> MoveSection(Guid courseId, Guid sectionId, MoveRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new MoveSectionCommand(courseId, sectionId, body.Direction), cancellationToken));

    [HttpPost("sections/{sectionId:guid}/activities")]
    public async Task<IActionResult> AddActivity(Guid courseId, Guid sectionId, ActivityRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(
            new AddActivityCommand(courseId, sectionId, body.Kind, body.TitleAr, body.TitleEn, body.IsRequired, body.BodyAr, body.BodyEn, body.VideoUrl, body.Form, body.Book, body.Lesson),
            cancellationToken));

    [HttpPost("sections/{sectionId:guid}/files")]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> AddFile(
        Guid courseId,
        Guid sectionId,
        [FromForm] string titleAr,
        [FromForm] string titleEn,
        [FromForm] bool isRequired,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Respond(await Dispatcher.Send(
            new AddFileActivityCommand(courseId, sectionId, titleAr, titleEn, isRequired, file.FileName, file.Length, stream),
            cancellationToken));
    }

    [HttpPost("sections/{sectionId:guid}/scorm")]
    [RequestSizeLimit(ScormPackage.MaxSize + (1024 * 1024))]
    [RequestFormLimits(MultipartBodyLengthLimit = ScormPackage.MaxSize + (1024 * 1024))]
    public async Task<IActionResult> AddScorm(
        Guid courseId,
        Guid sectionId,
        [FromForm] string titleAr,
        [FromForm] string titleEn,
        [FromForm] bool isRequired,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Respond(await Dispatcher.Send(
            new AddScormActivityCommand(
                string.Equals(Path.GetExtension(file.FileName), ".h5p", StringComparison.OrdinalIgnoreCase) ? ActivityKind.H5p : ActivityKind.Scorm,
                courseId, sectionId, titleAr, titleEn, isRequired, file.FileName, file.Length, stream),
            cancellationToken));
    }

    [HttpPut("activities/{activityId:guid}")]
    public async Task<IActionResult> UpdateActivity(Guid courseId, Guid activityId, ActivityRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(
            new UpdateActivityCommand(courseId, activityId, body.TitleAr, body.TitleEn, body.IsRequired, body.BodyAr, body.BodyEn, body.VideoUrl, body.Book, body.Lesson),
            cancellationToken));

    [HttpDelete("activities/{activityId:guid}")]
    public async Task<IActionResult> RemoveActivity(Guid courseId, Guid activityId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new RemoveActivityCommand(courseId, activityId), cancellationToken));

    [HttpGet("bin")]
    public async Task<IActionResult> Bin(Guid courseId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListBinQuery(courseId), cancellationToken));

    [HttpPost("bin/{itemId:guid}/restore")]
    public async Task<IActionResult> RestoreFromBin(Guid courseId, Guid itemId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new RestoreBinItemCommand(courseId, itemId), cancellationToken));

    [HttpDelete("bin/{itemId:guid}")]
    public async Task<IActionResult> PurgeFromBin(Guid courseId, Guid itemId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new PurgeBinItemCommand(courseId, itemId), cancellationToken));

    [HttpPost("activities/{activityId:guid}/move")]
    public async Task<IActionResult> MoveActivity(Guid courseId, Guid activityId, MoveRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new MoveActivityCommand(courseId, activityId, body.Direction), cancellationToken));
}

[Authorize]
[Route("api/v1/learning")]
public sealed class LearningController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("my")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyLearningQuery(), cancellationToken));

    [HttpGet("cohorts/{cohortId:guid}/player")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Player(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetPlayerQuery(cohortId), cancellationToken));

    [HttpPost("cohorts/{cohortId:guid}/activities/{activityId:guid}/complete")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> Complete(Guid cohortId, Guid activityId, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new CompleteActivityCommand(cohortId, activityId), cancellationToken));

    [HttpPost("cohorts/{cohortId:guid}/activities/{activityId:guid}/form")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> SubmitForm(Guid cohortId, Guid activityId, SubmitFormRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SubmitFormCommand(cohortId, activityId, body.Answers), cancellationToken));

    [HttpGet("cohorts/{cohortId:guid}/forms/export")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> ExportForms(Guid cohortId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportFormResultsQuery(cohortId), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpGet("cohorts/{cohortId:guid}/forms")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> FormResults(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListFormResultsQuery(cohortId), cancellationToken));

    [HttpPost("cohorts/{cohortId:guid}/activities/{activityId:guid}/scorm")]
    [Authorize(Roles = LmsRoles.Trainee)]
    public async Task<IActionResult> LaunchScorm(Guid cohortId, Guid activityId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new LaunchScormCommand(cohortId, activityId), cancellationToken));

    [HttpGet("activities/{activityId:guid}/file")]
    public async Task<IActionResult> Download(Guid activityId, [FromQuery] Guid? cohortId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new DownloadActivityFileQuery(activityId, cohortId), cancellationToken);

        // Naming the file forces a download, so uploads never run in this site's origin.
        return result.IsSuccess
            ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : Failure(result.Error!);
    }

    [HttpGet("cohorts/{cohortId:guid}/progress/export")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> ExportProgress(Guid cohortId, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new ExportCohortProgressQuery(cohortId), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, "text/csv; charset=utf-8", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpGet("cohorts/{cohortId:guid}/progress")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Progress(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCohortProgressQuery(cohortId), cancellationToken));

    [HttpGet("cohorts/{cohortId:guid}/attendance")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> Attendance(Guid cohortId, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCohortAttendanceQuery(cohortId), cancellationToken));

    [HttpPut("cohorts/{cohortId:guid}/attendance")]
    [Authorize(Roles = LmsRoles.CohortViewers)]
    public async Task<IActionResult> SaveAttendance(Guid cohortId, SaveAttendanceRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new SaveAttendanceCommand(cohortId, body.Date, body.Entries), cancellationToken));
}

/// <summary>
/// Reached only from the sandboxed frame. The sandbox gives the package no origin, so it cannot read this
/// site's cookies or pages. The signed link in the address is its one permission.
/// </summary>
[AllowAnonymous]
[Route("api/v1/scorm")]
public sealed class ScormController(IDispatcher dispatcher, IWebHostEnvironment host) : ApiController(dispatcher)
{
    private const int MaxBody = 256 * 1024;

    [HttpGet("{token}/{**path}")]
    public async Task<IActionResult> Asset(string token, string path, CancellationToken cancellationToken)
    {
        var result = path.StartsWith(H5pPackage.PlayerFiles, StringComparison.Ordinal)
            ? await PlayerFileAsync(token, path[H5pPackage.PlayerFiles.Length..], cancellationToken)
            : await Dispatcher.Send(new ReadScormAssetQuery(token, path), cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        Response.RegisterForDispose(result.Value.Owner);
        Response.Headers.Remove("X-Frame-Options");
        Response.Headers.ContentSecurityPolicy = "sandbox allow-scripts allow-forms; frame-ancestors 'self'";
        Response.Headers.AccessControlAllowOrigin = "*";
        Response.Headers.CacheControl = "private, no-store";
        return File(result.Value.Content, result.Value.ContentType);
    }

    /// <summary>Only names from the shipped player folder can match, so a crafted path reads nothing else.</summary>
    private async Task<Result<ScormAsset>> PlayerFileAsync(string token, string name, CancellationToken cancellationToken)
    {
        var valid = await Dispatcher.Send(new CheckScormLinkQuery(token), cancellationToken);
        if (!valid.IsSuccess)
        {
            return valid.Error!;
        }

        var file = name.Contains("..", StringComparison.Ordinal) ? null : host.WebRootFileProvider.GetFileInfo($"h5p-player/{name}");
        if (file is not { Exists: true, IsDirectory: false })
        {
            return ContentErrors.ActivityNotFound;
        }

        var type = Path.GetExtension(name) switch
        {
            ".js" => "text/javascript",
            ".css" => "text/css",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            _ => "application/octet-stream",
        };

        return new ScormAsset(file.CreateReadStream(), type, new MemoryStream());
    }

    [HttpPost("{token}/commit")]
    [RequestSizeLimit(MaxBody)]
    public async Task<IActionResult> Commit(string token, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return RespondNoContent(await Dispatcher.Send(new CommitScormCommand(token, body), cancellationToken));
    }
}
