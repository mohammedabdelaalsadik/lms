using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Learning.Application;

public sealed record PlayerActivity(
    Guid Id,
    ActivityKind Kind,
    TextDto Title,
    bool IsRequired,
    bool IsCompleted,
    bool IsLocked,
    string? BodyAr,
    string? BodyEn,
    string? VideoUrl,
    string? FileName,
    long? FileSize,
    FormDefinition? Form = null,
    BookDefinition? Book = null,
    LessonDefinition? Lesson = null);

public sealed record PlayerSection(Guid Id, TextDto Title, IReadOnlyList<PlayerActivity> Activities);

public sealed record PlayerResponse(
    Guid CohortId,
    string CourseCode,
    TextDto CourseTitle,
    TextDto CohortName,
    bool IsSequential,
    Guid? ResumeActivityId,
    int Percent,
    int RemainingActivities,
    int AttendancePercent,
    int MinAttendancePercent,
    bool AttendanceMet,
    int PassMarkPercent,
    decimal? FinalGradePercent,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PlayerSection> Sections,
    Guid CourseId = default);

public sealed record MyLearningItem(
    Guid CohortId,
    string CourseCode,
    TextDto CourseTitle,
    TextDto CohortName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    bool HasStarted,
    int Percent,
    DateTimeOffset? CompletedAt);

public sealed record ActivityFile(Stream Content, string FileName, string ContentType);

public sealed record CohortProgressEntry(
    Guid TraineeId,
    TextDto TraineeName,
    string MaskedNationalId,
    int Percent,
    int AttendancePercent,
    DateTimeOffset? CompletedAt);

public sealed record CohortProgressResponse(int MinAttendancePercent, IReadOnlyList<CohortProgressEntry> Entries);

internal static class ProgressMath
{
    /// <summary>Share of required activities done, or of all activities when none are required.</summary>
    public static int Percent(CourseContent? content, CourseProgress? progress)
    {
        if (content is null)
        {
            return 0;
        }

        var activities = content.OrderedActivities();
        var counted = activities.Any(a => a.IsRequired) ? [.. activities.Where(a => a.IsRequired)] : activities;

        if (counted.Count == 0)
        {
            return 0;
        }

        var done = progress?.CompletedActivities() ?? new HashSet<ActivityId>();
        return counted.Count(a => done.Contains(a.Id)) * 100 / counted.Count;
    }
}

public sealed record GetPlayerQuery(Guid CohortId) : IQuery<PlayerResponse>;

/// <summary>Shared gate: the trainee must be enrolled and the cohort must have started.</summary>
public sealed class TraineeGate(ICohortDirectory cohorts, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<CohortInfo>> EnterAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(cohortId, cancellationToken);
        if (cohort is null)
        {
            return ProgressErrors.CohortNotFound;
        }

        if (user.Id is not { } traineeId || !await cohorts.IsEnrolledAsync(cohortId, traineeId, cancellationToken))
        {
            return ProgressErrors.NotEnrolled;
        }

        if (DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) < cohort.StartsOn)
        {
            return ProgressErrors.NotStarted;
        }

        return cohort;
    }
}

internal sealed class GetPlayerHandler(
    TraineeGate gate,
    ICurrentUser user,
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    IAttendanceRepository attendance,
    ICourseCatalog catalog) : IRequestHandler<GetPlayerQuery, PlayerResponse>
{
    public async Task<Result<PlayerResponse>> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
    {
        var cohort = await gate.EnterAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var traineeId = user.Id!.Value;
        var content = await contents.FindByCourseAsync(cohort.Value.CourseId, cancellationToken);
        var progress = await progresses.FindAsync(request.CohortId, traineeId, cancellationToken);
        var tallies = await attendance.TallyAsync(request.CohortId, cancellationToken);
        var tally = tallies.GetValueOrDefault(traineeId, AttendanceTally.Empty);
        var courses = await catalog.GetAsync([cohort.Value.CourseId], cancellationToken);
        var course = courses.GetValueOrDefault(cohort.Value.CourseId);

        var completed = progress?.CompletedActivities() ?? new HashSet<ActivityId>();
        var order = content?.OrderedActivities().Select(a => a.Id).ToList() ?? [];
        var sequential = content?.IsSequential ?? false;
        var rule = content?.Rule() ?? new CompletionRule([], 0, 0);

        bool Locked(ActivityId id) =>
            sequential && !order.TakeWhile(a => a != id).All(completed.Contains);

        var sections = content?.OrderedSections().Select(s => new PlayerSection(
            s.Id.Value,
            new TextDto(s.Title.Ar, s.Title.En),
            [.. s.Activities.OrderBy(a => a.Position).Select(a =>
            {
                var locked = Locked(a.Id);

                // A locked activity reveals only its title.
                return new PlayerActivity(
                    a.Id.Value,
                    a.Kind,
                    new TextDto(a.Title.Ar, a.Title.En),
                    a.IsRequired,
                    completed.Contains(a.Id),
                    locked,
                    locked ? null : a.BodyAr,
                    locked ? null : a.BodyEn,
                    locked ? null : a.VideoUrl,
                    locked ? null : a.FileName,
                    locked ? null : a.FileSize,
                    locked ? null : FormCodec.ReadForm(a.FormJson),
                    locked || a.Kind != ActivityKind.Book ? null : FormCodec.ReadBook(a.StructureJson),
                    locked || a.Kind != ActivityKind.Lesson ? null : FormCodec.ReadLesson(a.StructureJson));
            })])).ToList() ?? [];

        var resume = order.Where(a => !completed.Contains(a)).Select(a => (Guid?)a.Value).FirstOrDefault() ?? order.Select(a => (Guid?)a.Value).FirstOrDefault();

        return new PlayerResponse(
            request.CohortId,
            course?.Code ?? "",
            new TextDto(course?.TitleAr ?? "", course?.TitleEn ?? ""),
            new TextDto(cohort.Value.NameAr, cohort.Value.NameEn),
            sequential,
            resume,
            ProgressMath.Percent(content, progress),
            rule.RemainingActivities(completed),
            rule.AttendancePercent(tally),
            rule.MinAttendancePercent,
            rule.AttendanceMet(tally),
            rule.PassMarkPercent,
            progress?.FinalGradePercent,
            progress?.CompletedAt,
            sections,
            cohort.Value.CourseId);
    }
}

public sealed record CompleteActivityCommand(Guid CohortId, Guid ActivityId) : ICommand<Unit>;

/// <summary>Marks one activity done and re-checks the course, for manual and SCORM completion alike.</summary>
public sealed class ActivityCompleter(
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    IAttendanceRepository attendance,
    TimeProvider clock)
{
    public async Task<Result> CompleteAsync(CohortInfo cohort, Guid traineeId, ActivityId activityId, CancellationToken cancellationToken)
    {
        var content = await contents.FindByCourseAsync(cohort.CourseId, cancellationToken);

        if (content?.FindActivity(activityId) is null)
        {
            return ContentErrors.ActivityNotFound;
        }

        var progress = await progresses.FindAsync(cohort.Id, traineeId, cancellationToken);

        if (progress is null)
        {
            progress = CourseProgress.Start(cohort.Id, cohort.CourseId, traineeId);
            progresses.Add(progress);
        }

        var now = clock.GetUtcNow();
        var order = content.OrderedActivities().Select(a => a.Id).ToList();
        var result = progress.CompleteActivity(activityId, order, content.IsSequential, now);

        if (!result.IsSuccess)
        {
            return result;
        }

        var tallies = await attendance.TallyAsync(cohort.Id, cancellationToken);
        progress.EvaluateCompletion(content.Rule(), tallies.GetValueOrDefault(traineeId, AttendanceTally.Empty), now);
        return Result.Success();
    }
}

internal sealed class CompleteActivityHandler(
    TraineeGate gate,
    ICurrentUser user,
    ICourseContentRepository contents,
    ActivityCompleter completer,
    ILearningUnitOfWork unitOfWork) : IRequestHandler<CompleteActivityCommand, Unit>
{
    public async Task<Result<Unit>> Handle(CompleteActivityCommand request, CancellationToken cancellationToken)
    {
        var cohort = await gate.EnterAsync(request.CohortId, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        var activityId = new ActivityId(request.ActivityId);
        var content = await contents.FindByCourseAsync(cohort.Value.CourseId, cancellationToken);

        if (content?.FindActivity(activityId) is { Kind: ActivityKind.Scorm })
        {
            return ContentErrors.ScormCompletesItself;
        }

        if (content?.FindActivity(activityId) is { Kind: ActivityKind.Form })
        {
            return FormErrors.FinishesOnSubmit;
        }

        var result = await completer.CompleteAsync(cohort.Value, user.Id!.Value, activityId, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record DownloadActivityFileQuery(Guid ActivityId, Guid? CohortId) : IQuery<ActivityFile>;

internal sealed class DownloadActivityFileHandler(
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    IFileStorage storage,
    LearningAccess access,
    TraineeGate gate,
    ICurrentUser user) : IRequestHandler<DownloadActivityFileQuery, ActivityFile>
{
    public async Task<Result<ActivityFile>> Handle(DownloadActivityFileQuery request, CancellationToken cancellationToken)
    {
        var activityId = new ActivityId(request.ActivityId);
        var content = await contents.FindByActivityAsync(activityId, cancellationToken);
        var activity = content?.FindActivity(activityId);

        if (content is null || activity?.FileKey is null)
        {
            return ContentErrors.ActivityNotFound;
        }

        if (!await access.CanEditCourseAsync(content.CourseId, cancellationToken))
        {
            var allowed = await CanTraineeOpenAsync(content, activityId, request.CohortId, cancellationToken);
            if (!allowed.IsSuccess)
            {
                return allowed.Error!;
            }
        }

        return new ActivityFile(storage.Open(activity.FileKey), activity.FileName!, activity.FileContentType!);
    }

    private async Task<Result> CanTraineeOpenAsync(CourseContent content, ActivityId activityId, Guid? cohortId, CancellationToken cancellationToken)
    {
        if (cohortId is not { } id)
        {
            return ContentErrors.Forbidden;
        }

        var cohort = await gate.EnterAsync(id, cancellationToken);
        if (!cohort.IsSuccess)
        {
            return cohort.Error!;
        }

        if (cohort.Value.CourseId != content.CourseId)
        {
            return ContentErrors.Forbidden;
        }

        var order = content.OrderedActivities().Select(a => a.Id).ToList();
        var progress = await progresses.FindAsync(id, user.Id!.Value, cancellationToken)
            ?? CourseProgress.Start(id, content.CourseId, user.Id.Value);

        return progress.CanOpen(activityId, order, content.IsSequential) ? Result.Success() : ProgressErrors.ActivityLocked;
    }
}

public sealed record ListMyLearningQuery : IQuery<IReadOnlyList<MyLearningItem>>;

internal sealed class ListMyLearningHandler(
    ICohortDirectory cohorts,
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    ICourseCatalog catalog,
    ICurrentUser user,
    TimeProvider clock) : IRequestHandler<ListMyLearningQuery, IReadOnlyList<MyLearningItem>>
{
    public async Task<Result<IReadOnlyList<MyLearningItem>>> Handle(ListMyLearningQuery request, CancellationToken cancellationToken)
    {
        var traineeId = user.Id!.Value;
        var enrolled = await cohorts.GetEnrolledCohortsAsync(traineeId, cancellationToken);
        var courseIds = enrolled.Select(c => c.CourseId).Distinct().ToList();

        var courses = await catalog.GetAsync(courseIds, cancellationToken);
        var content = (await contents.ListByCoursesAsync(courseIds, cancellationToken)).ToDictionary(c => c.CourseId);
        var progress = (await progresses.ListByTraineeAsync(traineeId, cancellationToken)).ToDictionary(p => p.CohortId);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        IReadOnlyList<MyLearningItem> items = [.. enrolled.Select(c =>
        {
            var course = courses.GetValueOrDefault(c.CourseId);
            var mine = progress.GetValueOrDefault(c.Id);

            return new MyLearningItem(
                c.Id,
                course?.Code ?? "",
                new TextDto(course?.TitleAr ?? "", course?.TitleEn ?? ""),
                new TextDto(c.NameAr, c.NameEn),
                c.StartsOn,
                c.EndsOn,
                today >= c.StartsOn,
                ProgressMath.Percent(content.GetValueOrDefault(c.CourseId), mine),
                mine?.CompletedAt);
        })];

        return Result<IReadOnlyList<MyLearningItem>>.Success(items);
    }
}

public sealed record GetCohortProgressQuery(Guid CohortId) : IQuery<CohortProgressResponse>;

public sealed record ExportCohortProgressQuery(Guid CohortId) : IQuery<CsvFile>, IAuditedRequest;

internal sealed class ExportCohortProgressHandler(IRequestHandler<GetCohortProgressQuery, CohortProgressResponse> progress) : IRequestHandler<ExportCohortProgressQuery, CsvFile>
{
    public async Task<Result<CsvFile>> Handle(ExportCohortProgressQuery request, CancellationToken cancellationToken)
    {
        var report = await progress.Handle(new GetCohortProgressQuery(request.CohortId), cancellationToken);
        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        IReadOnlyList<string?> header = ["الاسم / Name", "Name", "الهوية / ID", "التقدم / Progress %", "الحضور / Attendance %", "أكمل في / Completed at"];

        var rows = report.Value.Entries.Select(e => (IReadOnlyList<string?>)
        [
            e.TraineeName.Ar,
            e.TraineeName.En,
            e.MaskedNationalId,
            e.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            e.AttendancePercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            e.CompletedAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ]);

        return CsvWriter.Write($"progress-{request.CohortId:N}.csv", new[] { header }.Concat(rows));
    }
}

internal sealed class GetCohortProgressHandler(
    ICohortDirectory cohorts,
    LearningAccess access,
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    IAttendanceRepository attendance,
    IUserDirectory directory) : IRequestHandler<GetCohortProgressQuery, CohortProgressResponse>
{
    public async Task<Result<CohortProgressResponse>> Handle(GetCohortProgressQuery request, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(request.CohortId, cancellationToken);
        if (cohort is null)
        {
            return ProgressErrors.CohortNotFound;
        }

        if (!access.CanRunCohort(cohort))
        {
            return ContentErrors.Forbidden;
        }

        var trainees = await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken);
        var names = await directory.GetAsync(trainees, cancellationToken);
        var content = await contents.FindByCourseAsync(cohort.CourseId, cancellationToken);
        var rule = content?.Rule() ?? new CompletionRule([], 0, 0);
        var progress = (await progresses.ListByCohortAsync(request.CohortId, cancellationToken)).ToDictionary(p => p.TraineeId);
        var tallies = await attendance.TallyAsync(request.CohortId, cancellationToken);

        var entries = trainees.Select(id =>
        {
            var name = names.GetValueOrDefault(id);
            var mine = progress.GetValueOrDefault(id);

            return new CohortProgressEntry(
                id,
                new TextDto(name?.NameAr ?? "", name?.NameEn ?? ""),
                name?.MaskedNationalId ?? "",
                ProgressMath.Percent(content, mine),
                rule.AttendancePercent(tallies.GetValueOrDefault(id, AttendanceTally.Empty)),
                mine?.CompletedAt);
        }).ToList();

        return new CohortProgressResponse(rule.MinAttendancePercent, entries);
    }
}
