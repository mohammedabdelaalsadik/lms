using Lms.SharedKernel;

namespace Lms.Modules.Learning.Domain;

public readonly record struct CourseContentId(Guid Value);

public readonly record struct SectionId(Guid Value);

public readonly record struct ActivityId(Guid Value);

public enum ActivityKind { Page, File, Video, Scorm, H5p, Form, Book, Lesson }

public enum MoveDirection { Up, Down }

public static class ContentErrors
{
    public static readonly Error CourseNotFound = Error.NotFound("learning.course_not_found", "Course was not found.");
    public static readonly Error SectionNotFound = Error.NotFound("learning.section_not_found", "Section was not found.");
    public static readonly Error ActivityNotFound = Error.NotFound("learning.activity_not_found", "Activity was not found.");
    public static readonly Error InvalidAttendanceRule = Error.Validation("learning.invalid_attendance_rule", "An attendance percentage needs a number of planned sessions.");
    public static readonly Error InvalidVideoUrl = Error.Validation("learning.invalid_video_url", "Enter a full https link.");
    public static readonly Error BodyRequired = Error.Validation("learning.body_required", "Page text is required in Arabic and English.");
    public static readonly Error InvalidScormPackage = Error.Validation("learning.invalid_scorm_package", "This is not a valid SCORM package.");
    public static readonly Error ScormCompletesItself = Error.Conflict("learning.scorm_completes_itself", "This activity finishes when the package reports it.");
    public static readonly Error InvalidPassMark = Error.Validation("learning.invalid_pass_mark", "The pass mark must be between 0 and 100.");
    public static readonly Error Forbidden = Error.Forbidden("learning.forbidden", "You cannot do this for this course or cohort.");
}

public sealed record FileAsset(string StorageKey, string FileName, string ContentType, long Size);

public sealed class Activity : Entity<ActivityId>
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 20000;
    public const int UrlMaxLength = 500;

    private Activity()
    {
    }

    public LocalizedText Title { get; private set; } = null!;
    public ActivityKind Kind { get; private set; }
    public bool IsRequired { get; private set; }
    public int Position { get; internal set; }
    public string? BodyAr { get; private set; }
    public string? BodyEn { get; private set; }
    public string? VideoUrl { get; private set; }
    public string? FileKey { get; private set; }
    public string? FileName { get; private set; }
    public string? FileContentType { get; private set; }
    public long? FileSize { get; private set; }
    public string? ScormEntry { get; private set; }
    public string? FormJson { get; private set; }
    public string? StructureJson { get; private set; }

    internal static Result<Activity> Page(LocalizedText title, bool required, string? bodyAr, string? bodyEn)
    {
        if (string.IsNullOrWhiteSpace(bodyAr) || string.IsNullOrWhiteSpace(bodyEn))
        {
            return ContentErrors.BodyRequired;
        }

        var activity = New(title, ActivityKind.Page, required);
        activity.BodyAr = bodyAr.Trim();
        activity.BodyEn = bodyEn.Trim();
        return activity;
    }

    internal static Result<Activity> Video(LocalizedText title, bool required, string? url)
    {
        if (!IsSafeUrl(url))
        {
            return ContentErrors.InvalidVideoUrl;
        }

        var activity = New(title, ActivityKind.Video, required);
        activity.VideoUrl = url!.Trim();
        return activity;
    }

    internal static Activity Package(ActivityKind kind, LocalizedText title, bool required, FileAsset file, string entryPoint)
    {
        var activity = New(title, kind, required);
        activity.FileKey = file.StorageKey;
        activity.FileName = file.FileName;
        activity.FileContentType = file.ContentType;
        activity.FileSize = file.Size;
        activity.ScormEntry = entryPoint;
        return activity;
    }

    internal static Activity Structured(ActivityKind kind, LocalizedText title, bool required, string structureJson)
    {
        var activity = New(title, kind, required);
        activity.StructureJson = structureJson;
        return activity;
    }

    internal void ReplaceStructure(string structureJson) => StructureJson = structureJson;

    internal static Activity Form(LocalizedText title, bool required, string formJson)
    {
        var activity = New(title, ActivityKind.Form, required);
        activity.FormJson = formJson;
        return activity;
    }

    internal static Activity File(LocalizedText title, bool required, FileAsset file)
    {
        var activity = New(title, ActivityKind.File, required);
        activity.FileKey = file.StorageKey;
        activity.FileName = file.FileName;
        activity.FileContentType = file.ContentType;
        activity.FileSize = file.Size;
        return activity;
    }

    internal Result Update(LocalizedText title, bool required, string? bodyAr, string? bodyEn, string? url)
    {
        if (Kind == ActivityKind.Page && (string.IsNullOrWhiteSpace(bodyAr) || string.IsNullOrWhiteSpace(bodyEn)))
        {
            return ContentErrors.BodyRequired;
        }

        if (Kind == ActivityKind.Video && !IsSafeUrl(url))
        {
            return ContentErrors.InvalidVideoUrl;
        }

        Title = title;
        IsRequired = required;

        if (Kind == ActivityKind.Page)
        {
            BodyAr = bodyAr!.Trim();
            BodyEn = bodyEn!.Trim();
        }

        if (Kind == ActivityKind.Video)
        {
            VideoUrl = url!.Trim();
        }

        return Result.Success();
    }

    public ActivitySnapshot ToSnapshot() =>
        new(Id.Value, Title.Ar, Title.En, Kind, IsRequired, BodyAr, BodyEn, VideoUrl, FileKey, FileName, FileContentType, FileSize, ScormEntry, FormJson, StructureJson);

    internal static Activity FromSnapshot(ActivitySnapshot snapshot) => new()
    {
        Id = new ActivityId(snapshot.Id),
        Title = LocalizedText.Create(snapshot.TitleAr, snapshot.TitleEn, TitleMaxLength).Value,
        Kind = snapshot.Kind,
        IsRequired = snapshot.IsRequired,
        BodyAr = snapshot.BodyAr,
        BodyEn = snapshot.BodyEn,
        VideoUrl = snapshot.VideoUrl,
        FileKey = snapshot.FileKey,
        FileName = snapshot.FileName,
        FileContentType = snapshot.FileContentType,
        FileSize = snapshot.FileSize,
        ScormEntry = snapshot.ScormEntry,
        FormJson = snapshot.FormJson,
        StructureJson = snapshot.StructureJson,
    };

    private static Activity New(LocalizedText title, ActivityKind kind, bool required) => new()
    {
        Id = new ActivityId(Guid.CreateVersion7()),
        Title = title,
        Kind = kind,
        IsRequired = required,
    };

    private static bool IsSafeUrl(string? url) =>
        url?.Length <= UrlMaxLength
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}

public sealed class Section : Entity<SectionId>
{
    public const int TitleMaxLength = 200;

    private readonly List<Activity> _activities = [];

    private Section()
    {
    }

    public LocalizedText Title { get; private set; } = null!;
    public int Position { get; internal set; }

    public IReadOnlyCollection<Activity> Activities => _activities;

    internal static Section Create(LocalizedText title, int position) => new()
    {
        Id = new SectionId(Guid.CreateVersion7()),
        Title = title,
        Position = position,
    };

    internal static Section FromSnapshot(SectionSnapshot snapshot, int position)
    {
        var section = new Section
        {
            Id = new SectionId(snapshot.Id),
            Title = LocalizedText.Create(snapshot.TitleAr, snapshot.TitleEn, TitleMaxLength).Value,
            Position = position,
        };

        foreach (var activity in snapshot.Activities)
        {
            section.Add(Activity.FromSnapshot(activity));
        }

        return section;
    }

    internal void Rename(LocalizedText title) => Title = title;

    internal void Add(Activity activity)
    {
        activity.Position = _activities.Count == 0 ? 0 : _activities.Max(a => a.Position) + 1;
        _activities.Add(activity);
    }

    internal void Remove(Activity activity) => _activities.Remove(activity);
}

/// <summary>
/// The material of one course and the rules for finishing it. A course has one content,
/// shared by all of its cohorts. Rule changes never touch completions already recorded.
/// </summary>
public sealed class CourseContent : AggregateRoot<CourseContentId>
{
    private readonly List<Section> _sections = [];

    private CourseContent()
    {
    }

    public Guid CourseId { get; private set; }
    public bool IsSequential { get; private set; }
    public int MinAttendancePercent { get; private set; }
    public int PlannedSessions { get; private set; }
    public int PassMarkPercent { get; private set; }

    public IReadOnlyCollection<Section> Sections => _sections;

    public static CourseContent Create(Guid courseId) => new()
    {
        Id = new CourseContentId(Guid.CreateVersion7()),
        CourseId = courseId,
    };

    public IReadOnlyList<Section> OrderedSections() => [.. _sections.OrderBy(s => s.Position)];

    public IReadOnlyList<Activity> OrderedActivities() =>
        [.. OrderedSections().SelectMany(s => s.Activities.OrderBy(a => a.Position))];

    public CompletionRule Rule() => new(
        [.. _sections.SelectMany(s => s.Activities).Where(a => a.IsRequired).Select(a => a.Id)],
        MinAttendancePercent,
        PlannedSessions,
        PassMarkPercent);

    public Result UpdateSettings(bool isSequential, int minAttendancePercent, int plannedSessions, int passMarkPercent = 0)
    {
        if (passMarkPercent is < 0 or > 100)
        {
            return ContentErrors.InvalidPassMark;
        }

        var invalid = minAttendancePercent is < 0 or > 100
            || plannedSessions < 0
            || (minAttendancePercent > 0 && plannedSessions == 0);

        if (invalid)
        {
            return ContentErrors.InvalidAttendanceRule;
        }

        IsSequential = isSequential;
        MinAttendancePercent = minAttendancePercent;
        PlannedSessions = plannedSessions;
        PassMarkPercent = passMarkPercent;
        return Result.Success();
    }

    public Section AddSection(LocalizedText title)
    {
        var section = Section.Create(title, _sections.Count == 0 ? 0 : _sections.Max(s => s.Position) + 1);
        _sections.Add(section);
        return section;
    }

    public Result RenameSection(SectionId id, LocalizedText title)
    {
        var section = _sections.Find(s => s.Id == id);
        if (section is null)
        {
            return ContentErrors.SectionNotFound;
        }

        section.Rename(title);
        return Result.Success();
    }

    /// <summary>Returns the removed section's files so the caller can delete them from storage.</summary>
    public Result<IReadOnlyList<string>> RemoveSection(SectionId id)
    {
        var section = _sections.Find(s => s.Id == id);
        if (section is null)
        {
            return ContentErrors.SectionNotFound;
        }

        _sections.Remove(section);
        IReadOnlyList<string> files = [.. section.Activities.Where(a => a.FileKey is not null).Select(a => a.FileKey!)];
        return Result<IReadOnlyList<string>>.Success(files);
    }

    public Result MoveSection(SectionId id, MoveDirection direction)
    {
        var ordered = OrderedSections();
        var index = ordered.ToList().FindIndex(s => s.Id == id);
        if (index < 0)
        {
            return ContentErrors.SectionNotFound;
        }

        var other = direction == MoveDirection.Up ? index - 1 : index + 1;
        if (other >= 0 && other < ordered.Count)
        {
            (ordered[index].Position, ordered[other].Position) = (ordered[other].Position, ordered[index].Position);
        }

        return Result.Success();
    }

    public Result AddPage(SectionId sectionId, LocalizedText title, bool required, string? bodyAr, string? bodyEn) =>
        AddTo(sectionId, Activity.Page(title, required, bodyAr, bodyEn));

    public Result AddStructured(ActivityKind kind, SectionId sectionId, LocalizedText title, bool required, string structureJson) =>
        AddTo(sectionId, Activity.Structured(kind, title, required, structureJson));

    public void ReplaceStructure(ActivityId id, string structureJson) => FindActivity(id)?.ReplaceStructure(structureJson);

    public Result AddForm(SectionId sectionId, LocalizedText title, bool required, string formJson) =>
        AddTo(sectionId, Activity.Form(title, required, formJson));

    public Result AddVideo(SectionId sectionId, LocalizedText title, bool required, string? url) =>
        AddTo(sectionId, Activity.Video(title, required, url));

    public Result AddFile(SectionId sectionId, LocalizedText title, bool required, FileAsset file) =>
        AddTo(sectionId, Activity.File(title, required, file));

    public Result AddPackage(ActivityKind kind, SectionId sectionId, LocalizedText title, bool required, FileAsset file, string entryPoint) =>
        AddTo(sectionId, Activity.Package(kind, title, required, file, entryPoint));

    public Result UpdateActivity(ActivityId id, LocalizedText title, bool required, string? bodyAr, string? bodyEn, string? url)
    {
        var activity = FindActivity(id);
        return activity is null ? ContentErrors.ActivityNotFound : activity.Update(title, required, bodyAr, bodyEn, url);
    }

    /// <summary>Returns the removed file, if any, so the caller can delete it from storage.</summary>
    public Result<string?> RemoveActivity(ActivityId id)
    {
        var section = _sections.Find(s => s.Activities.Any(a => a.Id == id));
        if (section is null)
        {
            return ContentErrors.ActivityNotFound;
        }

        var activity = section.Activities.First(a => a.Id == id);
        section.Remove(activity);
        return Result<string?>.Success(activity.FileKey);
    }

    public SectionSnapshot? SnapshotSection(SectionId id)
    {
        var section = _sections.Find(s => s.Id == id);
        return section is null
            ? null
            : new SectionSnapshot(id.Value, section.Title.Ar, section.Title.En, [.. section.Activities.OrderBy(a => a.Position).Select(a => a.ToSnapshot())]);
    }

    public (ActivitySnapshot Activity, SectionId Section)? SnapshotActivity(ActivityId id)
    {
        var section = _sections.Find(s => s.Activities.Any(a => a.Id == id));
        return section is null ? null : (section.Activities.First(a => a.Id == id).ToSnapshot(), section.Id);
    }

    public Result RestoreSection(SectionSnapshot snapshot)
    {
        _sections.Add(Section.FromSnapshot(snapshot, _sections.Count == 0 ? 0 : _sections.Max(s => s.Position) + 1));
        return Result.Success();
    }

    /// <summary>Goes back to its own section, or to the last one if that was removed too.</summary>
    public Result RestoreActivity(ActivitySnapshot snapshot, SectionId sectionId)
    {
        var section = _sections.Find(s => s.Id == sectionId) ?? OrderedSections().LastOrDefault();
        if (section is null)
        {
            return BinErrors.NoSection;
        }

        section.Add(Activity.FromSnapshot(snapshot));
        return Result.Success();
    }

    public Result MoveActivity(ActivityId id, MoveDirection direction)
    {
        var section = _sections.Find(s => s.Activities.Any(a => a.Id == id));
        if (section is null)
        {
            return ContentErrors.ActivityNotFound;
        }

        var ordered = section.Activities.OrderBy(a => a.Position).ToList();
        var index = ordered.FindIndex(a => a.Id == id);
        var other = direction == MoveDirection.Up ? index - 1 : index + 1;

        if (other >= 0 && other < ordered.Count)
        {
            (ordered[index].Position, ordered[other].Position) = (ordered[other].Position, ordered[index].Position);
        }

        return Result.Success();
    }

    public Activity? FindActivity(ActivityId id) =>
        _sections.SelectMany(s => s.Activities).FirstOrDefault(a => a.Id == id);

    private Result AddTo(SectionId sectionId, Result<Activity> activity)
    {
        var section = _sections.Find(s => s.Id == sectionId);
        if (section is null)
        {
            return ContentErrors.SectionNotFound;
        }

        if (!activity.IsSuccess)
        {
            return activity.Error!;
        }

        section.Add(activity.Value);
        return Result.Success();
    }
}
