using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Learning.Application;

public sealed record TextDto(string Ar, string En);

public sealed record ActivityResponse(
    Guid Id,
    ActivityKind Kind,
    TextDto Title,
    bool IsRequired,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    string? FileName,
    long? FileSize,
    FormDefinition? Form = null,
    BookDefinition? Book = null,
    LessonDefinition? Lesson = null);

public sealed record SectionResponse(Guid Id, TextDto Title, IReadOnlyList<ActivityResponse> Activities);

public sealed record CourseContentResponse(
    Guid CourseId,
    string CourseCode,
    TextDto CourseTitle,
    bool IsSequential,
    int MinAttendancePercent,
    int PlannedSessions,
    int PassMarkPercent,
    IReadOnlyList<SectionResponse> Sections);

/// <summary>Opens a course's content for editing, after checking access, and answers with the saved result.</summary>
public sealed class ContentEditor(
    ICourseContentRepository contents,
    ICourseCatalog catalog,
    LearningAccess access,
    ILearningUnitOfWork unitOfWork)
{
    public async Task<Result<CourseContent>> OpenAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await FindCourseAsync(courseId, cancellationToken);
        if (course is null)
        {
            return ContentErrors.CourseNotFound;
        }

        if (!await access.CanEditCourseAsync(courseId, cancellationToken))
        {
            return ContentErrors.Forbidden;
        }

        var content = await contents.FindByCourseAsync(courseId, cancellationToken);
        if (content is null)
        {
            content = CourseContent.Create(courseId);
            contents.Add(content);
        }

        return content;
    }

    public async Task<Result<CourseContentResponse>> FinishAsync(CourseContent content, Result outcome, CancellationToken cancellationToken)
    {
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(content, cancellationToken);
    }

    public async Task<CourseContentResponse> DescribeAsync(CourseContent content, CancellationToken cancellationToken)
    {
        var course = await FindCourseAsync(content.CourseId, cancellationToken);

        return new CourseContentResponse(
            content.CourseId,
            course?.Code ?? "",
            new TextDto(course?.TitleAr ?? "", course?.TitleEn ?? ""),
            content.IsSequential,
            content.MinAttendancePercent,
            content.PlannedSessions,
            content.PassMarkPercent,
            [.. content.OrderedSections().Select(s => new SectionResponse(
                s.Id.Value,
                new TextDto(s.Title.Ar, s.Title.En),
                [.. s.Activities.OrderBy(a => a.Position).Select(a => new ActivityResponse(
                    a.Id.Value,
                    a.Kind,
                    new TextDto(a.Title.Ar, a.Title.En),
                    a.IsRequired,
                    a.BodyAr,
                    a.BodyEn,
                    a.VideoUrl,
                    a.FileName,
                    a.FileSize,
                    FormCodec.ReadForm(a.FormJson),
                    a.Kind == ActivityKind.Book ? FormCodec.ReadBook(a.StructureJson) : null,
                    a.Kind == ActivityKind.Lesson ? FormCodec.ReadLesson(a.StructureJson) : null))]))]);
    }

    private async Task<CourseInfo?> FindCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var courses = await catalog.GetAsync([courseId], cancellationToken);
        return courses.GetValueOrDefault(courseId);
    }
}

public sealed record GetCourseContentQuery(Guid CourseId) : IQuery<CourseContentResponse>;

internal sealed class GetCourseContentHandler(ContentEditor editor)
    : IRequestHandler<GetCourseContentQuery, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(GetCourseContentQuery request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        return await editor.DescribeAsync(content.Value, cancellationToken);
    }
}

public sealed record UpdateContentSettingsCommand(Guid CourseId, bool IsSequential, int MinAttendancePercent, int PlannedSessions, int PassMarkPercent)
    : ICommand<CourseContentResponse>;

internal sealed class UpdateContentSettingsHandler(ContentEditor editor) : IRequestHandler<UpdateContentSettingsCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(UpdateContentSettingsCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var outcome = content.Value.UpdateSettings(request.IsSequential, request.MinAttendancePercent, request.PlannedSessions, request.PassMarkPercent);
        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record SaveSectionCommand(Guid CourseId, Guid? SectionId, string TitleAr, string TitleEn) : ICommand<CourseContentResponse>;

internal sealed class SaveSectionValidator : AbstractValidator<SaveSectionCommand>
{
    public SaveSectionValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Section.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Section.TitleMaxLength);
    }
}

internal sealed class SaveSectionHandler(ContentEditor editor) : IRequestHandler<SaveSectionCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(SaveSectionCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Section.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        Result outcome;
        if (request.SectionId is { } id)
        {
            outcome = content.Value.RenameSection(new SectionId(id), title.Value);
        }
        else
        {
            content.Value.AddSection(title.Value);
            outcome = Result.Success();
        }

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record RemoveSectionCommand(Guid CourseId, Guid SectionId) : ICommand<CourseContentResponse>;

internal sealed class RemoveSectionHandler(ContentEditor editor, RecycleBin bin) : IRequestHandler<RemoveSectionCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(RemoveSectionCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var id = new SectionId(request.SectionId);
        var snapshot = content.Value.SnapshotSection(id);
        var removed = content.Value.RemoveSection(id);

        if (snapshot is not null)
        {
            bin.Hold(request.CourseId, snapshot);
        }

        return await editor.FinishAsync(content.Value, removed, cancellationToken);
    }
}

public sealed record MoveSectionCommand(Guid CourseId, Guid SectionId, MoveDirection Direction) : ICommand<CourseContentResponse>;

internal sealed class MoveSectionHandler(ContentEditor editor) : IRequestHandler<MoveSectionCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(MoveSectionCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var outcome = content.Value.MoveSection(new SectionId(request.SectionId), request.Direction);
        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record AddActivityCommand(
    Guid CourseId,
    Guid SectionId,
    ActivityKind Kind,
    string TitleAr,
    string TitleEn,
    bool IsRequired,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    FormDefinition? Form = null,
    BookDefinition? Book = null,
    LessonDefinition? Lesson = null) : ICommand<CourseContentResponse>;

internal sealed class AddActivityValidator : AbstractValidator<AddActivityCommand>
{
    public AddActivityValidator()
    {
        RuleFor(c => c.Kind).Must(k => k is ActivityKind.Page or ActivityKind.Video or ActivityKind.Form or ActivityKind.Book or ActivityKind.Lesson);
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.BodyAr).MaximumLength(Activity.BodyMaxLength);
        RuleFor(c => c.BodyEn).MaximumLength(Activity.BodyMaxLength);
    }
}

internal sealed class AddActivityHandler(ContentEditor editor) : IRequestHandler<AddActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(AddActivityCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Activity.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var section = new SectionId(request.SectionId);
        if (request.Kind == ActivityKind.Form && request.Form is not { IsValid: true })
        {
            return FormErrors.InvalidForm;
        }

        var structure = FormCodec.WriteStructure(request.Kind, request.Book, request.Lesson);
        if (request.Kind is ActivityKind.Book or ActivityKind.Lesson && structure is null)
        {
            return request.Kind == ActivityKind.Book ? ReadingErrors.InvalidBook : ReadingErrors.InvalidLesson;
        }

        var outcome = request.Kind switch
        {
            ActivityKind.Book or ActivityKind.Lesson => content.Value.AddStructured(request.Kind, section, title.Value, request.IsRequired, structure!),
            ActivityKind.Page => content.Value.AddPage(section, title.Value, request.IsRequired, request.BodyAr, request.BodyEn),
            ActivityKind.Form => content.Value.AddForm(section, title.Value, request.IsRequired, FormCodec.Write(request.Form)),
            _ => content.Value.AddVideo(section, title.Value, request.IsRequired, request.VideoUrl),
        };

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record AddFileActivityCommand(
    Guid CourseId,
    Guid SectionId,
    string TitleAr,
    string TitleEn,
    bool IsRequired,
    string FileName,
    long Size,
    Stream Content) : ICommand<CourseContentResponse>;

internal sealed class AddFileActivityValidator : AbstractValidator<AddFileActivityCommand>
{
    public AddFileActivityValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Activity.TitleMaxLength);
    }
}

internal sealed class AddFileActivityHandler(ContentEditor editor, IFileStorage storage) : IRequestHandler<AddFileActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(AddFileActivityCommand request, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(request.FileName);
        var contentType = FileRules.ContentTypeFor(fileName);

        if (contentType is null || fileName.Length > 255)
        {
            return FileErrors.TypeNotAllowed;
        }

        if (request.Size is <= 0 or > FileRules.MaxSize)
        {
            return FileErrors.TooLarge;
        }

        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Activity.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var key = await storage.SaveAsync(request.Content, cancellationToken);
        var asset = new FileAsset(key, fileName, contentType, request.Size);
        var outcome = content.Value.AddFile(new SectionId(request.SectionId), title.Value, request.IsRequired, asset);

        if (!outcome.IsSuccess)
        {
            storage.Delete(key);
        }

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record UpdateActivityCommand(
    Guid CourseId,
    Guid ActivityId,
    string TitleAr,
    string TitleEn,
    bool IsRequired,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    BookDefinition? Book = null,
    LessonDefinition? Lesson = null) : ICommand<CourseContentResponse>;

internal sealed class UpdateActivityValidator : AbstractValidator<UpdateActivityCommand>
{
    public UpdateActivityValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(Activity.TitleMaxLength);
        RuleFor(c => c.BodyAr).MaximumLength(Activity.BodyMaxLength);
        RuleFor(c => c.BodyEn).MaximumLength(Activity.BodyMaxLength);
    }
}

internal sealed class UpdateActivityHandler(ContentEditor editor) : IRequestHandler<UpdateActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(UpdateActivityCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, Activity.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var id = new ActivityId(request.ActivityId);
        if (content.Value.FindActivity(id) is { Kind: ActivityKind.Book or ActivityKind.Lesson } structured)
        {
            var structure = FormCodec.WriteStructure(structured.Kind, request.Book, request.Lesson);
            if (structure is null)
            {
                return structured.Kind == ActivityKind.Book ? ReadingErrors.InvalidBook : ReadingErrors.InvalidLesson;
            }

            content.Value.ReplaceStructure(id, structure);
        }

        var outcome = content.Value.UpdateActivity(id, title.Value, request.IsRequired, request.BodyAr, request.BodyEn, request.VideoUrl);

        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}

public sealed record RemoveActivityCommand(Guid CourseId, Guid ActivityId) : ICommand<CourseContentResponse>;

internal sealed class RemoveActivityHandler(ContentEditor editor, RecycleBin bin) : IRequestHandler<RemoveActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(RemoveActivityCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var id = new ActivityId(request.ActivityId);
        var snapshot = content.Value.SnapshotActivity(id);
        var removed = content.Value.RemoveActivity(id);

        if (snapshot is { } held)
        {
            bin.Hold(request.CourseId, held.Section, held.Activity);
        }

        return await editor.FinishAsync(content.Value, removed, cancellationToken);
    }
}

public sealed record MoveActivityCommand(Guid CourseId, Guid ActivityId, MoveDirection Direction) : ICommand<CourseContentResponse>;

internal sealed class MoveActivityHandler(ContentEditor editor) : IRequestHandler<MoveActivityCommand, CourseContentResponse>
{
    public async Task<Result<CourseContentResponse>> Handle(MoveActivityCommand request, CancellationToken cancellationToken)
    {
        var content = await editor.OpenAsync(request.CourseId, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var outcome = content.Value.MoveActivity(new ActivityId(request.ActivityId), request.Direction);
        return await editor.FinishAsync(content.Value, outcome, cancellationToken);
    }
}
