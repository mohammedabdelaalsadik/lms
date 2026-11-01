using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Contracts;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lms.Modules.Learning.Application;

public static class BackupErrors
{
    public static readonly Error Invalid = Error.Validation("learning.backup_invalid", "This is not a valid course backup.");
    public static readonly Error CourseRefused = Error.Conflict("learning.backup_course_refused", "This course code is in use or not valid.");
}

public sealed record BackupActivity(ActivityKind Kind, string TitleAr, string TitleEn, bool IsRequired, string? BodyAr, string? BodyEn, string? VideoUrl, string? FileName, string? Entry, FormDefinition? Form = null, BookDefinition? Book = null, LessonDefinition? Lesson = null);

public sealed record BackupSection(string TitleAr, string TitleEn, IReadOnlyList<BackupActivity> Activities);

public sealed record BackupManifest(
    int Version,
    CourseDraft Course,
    bool IsSequential,
    int MinAttendancePercent,
    int PlannedSessions,
    int PassMarkPercent,
    IReadOnlyList<BackupSection> Sections,
    IReadOnlyList<BankEntry> Questions,
    IReadOnlyList<BackupTerm>? Glossary = null);

public sealed record BackupTerm(string TermAr, string TermEn, string DefinitionAr, string DefinitionEn);

/// <summary>A backup is a zip holding manifest.json and one numbered entry per file. Nothing else in the zip is ever read.</summary>
public static class BackupFormat
{
    public const int CurrentVersion = 1;
    public const string ManifestName = "manifest.json";
    public const long MaxManifestSize = 32 * 1024 * 1024;
    public const long MaxBackupSize = 1024L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static byte[] Serialize(BackupManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Options);

    /// <summary>Null when the text cannot be a backup of this version.</summary>
    public static BackupManifest? Parse(Stream content)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(content, Options);
            return manifest is { Version: CurrentVersion, Course: not null, Sections: not null, Questions: not null }
                && manifest.Sections.All(s => s.Activities is not null)
                ? manifest
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Copies an entry to a temporary file, stopping at the limit whatever the zip claims its size is.</summary>
    public static async Task<Stream?> ExtractAsync(ZipArchive zip, string? name, long limit, CancellationToken cancellationToken)
    {
        var entry = name is null ? null : zip.GetEntry(name);
        if (entry is null || entry.Length > limit)
        {
            return null;
        }

        var copy = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        await using var source = entry.Open();
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                await copy.DisposeAsync();
                return null;
            }

            await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        copy.Position = 0;
        return copy;
    }
}

public sealed record BackupFile(Stream Content, string FileName);

public sealed record ExportCourseQuery(Guid CourseId) : IQuery<BackupFile>, IAuditedRequest;

internal sealed class ExportCourseHandler(
    ICourseCatalog catalog,
    ICourseContentRepository contents,
    IQuestionBankTransfer bank,
    IGlossaryRepository glossary,
    IFileStorage storage) : IRequestHandler<ExportCourseQuery, BackupFile>
{
    public async Task<Result<BackupFile>> Handle(ExportCourseQuery request, CancellationToken cancellationToken)
    {
        var course = await catalog.GetDraftAsync(request.CourseId, cancellationToken);
        if (course is null)
        {
            return ContentErrors.CourseNotFound;
        }

        var content = await contents.FindByCourseAsync(request.CourseId, cancellationToken) ?? CourseContent.Create(request.CourseId);
        var output = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var number = 0;
            var sections = new List<BackupSection>();

            foreach (var section in content.OrderedSections())
            {
                var activities = new List<BackupActivity>();

                foreach (var activity in section.Activities.OrderBy(a => a.Position))
                {
                    string? entryName = null;
                    if (activity.FileKey is not null)
                    {
                        entryName = $"files/{++number}";
                        await using var source = storage.Open(activity.FileKey);
                        await using var target = zip.CreateEntry(entryName, CompressionLevel.Fastest).Open();
                        await source.CopyToAsync(target, cancellationToken);
                    }

                    activities.Add(new BackupActivity(activity.Kind, activity.Title.Ar, activity.Title.En, activity.IsRequired, activity.BodyAr, activity.BodyEn, activity.VideoUrl, activity.FileName, entryName, FormCodec.ReadForm(activity.FormJson),
                        activity.Kind == ActivityKind.Book ? FormCodec.ReadBook(activity.StructureJson) : null,
                        activity.Kind == ActivityKind.Lesson ? FormCodec.ReadLesson(activity.StructureJson) : null));
                }

                sections.Add(new BackupSection(section.Title.Ar, section.Title.En, activities));
            }

            var questions = await bank.ExportAsync(request.CourseId, cancellationToken);
            var terms = (await glossary.ListAsync(request.CourseId, cancellationToken)).Where(e => e.IsApproved).Select(e => new BackupTerm(e.Term.Ar, e.Term.En, e.Definition.Ar, e.Definition.En)).ToList();
            var manifest = new BackupManifest(BackupFormat.CurrentVersion, course, content.IsSequential, content.MinAttendancePercent, content.PlannedSessions, content.PassMarkPercent, sections, questions, terms);
            await using var writer = zip.CreateEntry(BackupFormat.ManifestName).Open();
            await writer.WriteAsync(BackupFormat.Serialize(manifest), cancellationToken);
        }

        output.Position = 0;
        return new BackupFile(output, $"{course.Code}-backup.zip");
    }
}

public sealed record RestoreResult(Guid CourseId, string Code, int Questions);

public sealed record RestoreCourseCommand(string Code, Stream Content) : ICommand<RestoreResult>;

internal sealed class RestoreCourseValidator : AbstractValidator<RestoreCourseCommand>
{
    public RestoreCourseValidator() => RuleFor(c => c.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
}

/// <summary>
/// Always creates a new draft course with new ids, and never touches an existing one. Everything goes through
/// the same domain rules and package checks as an upload, because a backup file can be edited by hand.
/// </summary>
internal sealed class RestoreCourseHandler(
    ICourseCatalog catalog,
    ICourseContentRepository contents,
    IQuestionBankTransfer bank,
    IGlossaryRepository glossary,
    IFileStorage storage,
    ICurrentUser user,
    ILearningUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RestoreCourseCommand, RestoreResult>
{
    public async Task<Result<RestoreResult>> Handle(RestoreCourseCommand request, CancellationToken cancellationToken)
    {
        var saved = new List<string>();

        try
        {
            using var zip = new ZipArchive(request.Content, ZipArchiveMode.Read, leaveOpen: true);
            await using var manifestStream = await BackupFormat.ExtractAsync(zip, BackupFormat.ManifestName, BackupFormat.MaxManifestSize, cancellationToken);
            var manifest = manifestStream is null ? null : BackupFormat.Parse(manifestStream);
            if (manifest is null)
            {
                return BackupErrors.Invalid;
            }

            var courseId = Guid.CreateVersion7();
            var content = CourseContent.Create(courseId);
            var settings = content.UpdateSettings(manifest.IsSequential, manifest.MinAttendancePercent, manifest.PlannedSessions, manifest.PassMarkPercent);
            if (!settings.IsSuccess)
            {
                return BackupErrors.Invalid;
            }

            foreach (var part in manifest.Sections)
            {
                var title = LocalizedText.Create(part.TitleAr, part.TitleEn, Section.TitleMaxLength);
                if (!title.IsSuccess)
                {
                    return Discard(saved);
                }

                var section = content.AddSection(title.Value);
                foreach (var activity in part.Activities)
                {
                    if (!await AddAsync(content, section.Id, activity, zip, saved, cancellationToken))
                    {
                        return Discard(saved);
                    }
                }
            }

            if (!await catalog.CreateDraftAsync(courseId, manifest.Course with { Code = request.Code }, cancellationToken))
            {
                Discard(saved);
                return BackupErrors.CourseRefused;
            }

            contents.Add(content);

            foreach (var entry in manifest.Glossary ?? [])
            {
                var term = LocalizedText.Create(entry.TermAr, entry.TermEn, GlossaryEntry.TermMaxLength);
                var definition = LocalizedText.Create(entry.DefinitionAr, entry.DefinitionEn, GlossaryEntry.DefinitionMaxLength);
                if (term.IsSuccess && definition.IsSuccess)
                {
                    glossary.Add(GlossaryEntry.Create(courseId, term.Value, definition.Value, true, user.Id!.Value, clock.GetUtcNow()));
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            var questions = await bank.ImportAsync(courseId, manifest.Questions, user.Id!.Value, cancellationToken);
            return new RestoreResult(courseId, request.Code.Trim().ToUpperInvariant(), questions);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or EndOfStreamException)
        {
            return Discard(saved);
        }
    }

    private async Task<bool> AddAsync(CourseContent content, SectionId section, BackupActivity activity, ZipArchive zip, List<string> saved, CancellationToken cancellationToken)
    {
        var title = LocalizedText.Create(activity.TitleAr, activity.TitleEn, Activity.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return false;
        }

        switch (activity.Kind)
        {
            case ActivityKind.Page:
                return content.AddPage(section, title.Value, activity.IsRequired, activity.BodyAr, activity.BodyEn).IsSuccess;

            case ActivityKind.Video:
                return content.AddVideo(section, title.Value, activity.IsRequired, activity.VideoUrl).IsSuccess;

            case ActivityKind.Book:
            case ActivityKind.Lesson:
                return FormCodec.WriteStructure(activity.Kind, activity.Book, activity.Lesson) is { } structure
                    && content.AddStructured(activity.Kind, section, title.Value, activity.IsRequired, structure).IsSuccess;

            case ActivityKind.Form:
                return activity.Form is { IsValid: true } && content.AddForm(section, title.Value, activity.IsRequired, FormCodec.Write(activity.Form)).IsSuccess;

            case ActivityKind.File:
            {
                var fileName = Path.GetFileName(activity.FileName ?? "");
                var type = FileRules.ContentTypeFor(fileName);
                await using var file = await BackupFormat.ExtractAsync(zip, activity.Entry, FileRules.MaxSize, cancellationToken);
                if (type is null || fileName.Length > 255 || file is null)
                {
                    return false;
                }

                var size = file.Length;
                var key = await storage.SaveAsync(file, cancellationToken);
                saved.Add(key);
                return content.AddFile(section, title.Value, activity.IsRequired, new FileAsset(key, fileName, type, size)).IsSuccess;
            }

            default:
            {
                await using var package = await BackupFormat.ExtractAsync(zip, activity.Entry, ScormPackage.MaxSize, cancellationToken);
                if (package is null)
                {
                    return false;
                }

                var entryPoint = activity.Kind == ActivityKind.H5p
                    ? (H5pPackage.IsValid(package) ? H5pPackage.PlayerPage : null)
                    : ScormPackage.Inspect(package)?.EntryPoint;

                if (entryPoint is null)
                {
                    return false;
                }

                package.Position = 0;
                var size = package.Length;
                var key = await storage.SaveAsync(package, cancellationToken);
                saved.Add(key);
                var fileName = Path.GetFileName(activity.FileName ?? "package.zip");
                return content.AddPackage(activity.Kind, section, title.Value, activity.IsRequired, new FileAsset(key, fileName, "application/zip", size), entryPoint).IsSuccess;
            }
        }
    }

    private Error Discard(List<string> saved)
    {
        foreach (var key in saved)
        {
            storage.Delete(key);
        }

        return BackupErrors.Invalid;
    }
}
