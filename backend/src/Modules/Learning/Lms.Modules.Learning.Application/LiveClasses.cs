using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Learning.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.Security.Cryptography;
using System.Text;

namespace Lms.Modules.Learning.Application;

public interface ILiveClassRepository
{
    Task<LiveClass?> FindAsync(LiveClassId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveClass>> ListAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveAttendance>> ListAttendanceAsync(IReadOnlyCollection<LiveClassId> ids, CancellationToken cancellationToken);

    Task<bool> HasAttendedAsync(LiveClassId id, Guid traineeId, CancellationToken cancellationToken);

    void Add(LiveClass live);

    void Add(LiveAttendance attendance);

    void Remove(LiveClass live);
}

public sealed record LiveMeeting(string MeetingId, string Name, int DurationMinutes);

/// <summary>The class server. Everything stays behind this port, so another provider could replace it.</summary>
public interface ILiveClassServer
{
    bool IsConfigured { get; }

    /// <summary>Safe to repeat: the server keeps the same meeting for the same id. False when it cannot be reached.</summary>
    Task<bool> OpenAsync(LiveMeeting meeting, CancellationToken cancellationToken);

    string JoinUrl(string meetingId, string fullName, Guid userId, bool moderator);

    Task<bool> CloseAsync(string meetingId, CancellationToken cancellationToken);
}

public static class BigBlueButtonUrl
{
    /// <summary>Each call is signed with the hash of its name, its query and the shared secret. The secret itself is never sent.</summary>
    public static string Build(string baseUrl, string secret, string algorithm, string call, IEnumerable<(string Key, string Value)> parameters)
    {
        var query = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        var signed = Sign(algorithm, call + query + secret);
        var separator = query.Length == 0 ? "" : "&";
        return $"{baseUrl.TrimEnd('/')}/{call}?{query}{separator}checksum={signed}";
    }

    private static string Sign(string algorithm, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexStringLower(algorithm.ToLowerInvariant() switch
        {
            "sha1" => SHA1.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes),
        });
    }
}

public sealed class LiveClassSchedule(ILiveClassRepository classes, TimeProvider clock) : ILiveClassSchedule
{
    public async Task<IReadOnlyList<UpcomingLiveClass>> ListOpenAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var list = await classes.ListAsync(cohortId, cancellationToken);
        return [.. list.Where(c => c.StatusAt(now) != LiveClassStatus.Ended).Select(c => new UpcomingLiveClass(c.Title.Ar, c.Title.En, c.StartsAt))];
    }
}

public sealed record LiveAttendee(TextDto Name, DateTimeOffset JoinedAt);

public sealed record LiveClassItem(Guid Id, TextDto Title, DateTimeOffset StartsAt, int DurationMinutes, LiveClassStatus Status, bool CanStart, IReadOnlyList<LiveAttendee> Attendees);

public sealed record LiveClassList(bool IsConfigured, bool CanManage, TextDto CohortName, IReadOnlyList<LiveClassItem> Classes);

/// <summary>A cohort's classes are for its trainees, its instructor and the staff who run it.</summary>
public sealed class LiveClassAccess(ICohortDirectory cohorts, LearningAccess access, ICurrentUser user)
{
    public async Task<Result<(CohortInfo Cohort, bool CanManage)>> EnterAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(cohortId, cancellationToken);
        if (cohort is null)
        {
            return ProgressErrors.CohortNotFound;
        }

        if (access.CanRunCohort(cohort))
        {
            return (cohort, true);
        }

        var enrolled = user.Id is { } id && await cohorts.IsEnrolledAsync(cohortId, id, cancellationToken);
        return enrolled ? (cohort, false) : ProgressErrors.NotEnrolled;
    }
}

public sealed record ListLiveClassesQuery(Guid CohortId) : IQuery<LiveClassList>;

internal sealed class ListLiveClassesHandler(
    LiveClassAccess access,
    ILiveClassRepository classes,
    ILiveClassServer server,
    IUserDirectory directory,
    TimeProvider clock) : IRequestHandler<ListLiveClassesQuery, LiveClassList>
{
    public async Task<Result<LiveClassList>> Handle(ListLiveClassesQuery request, CancellationToken cancellationToken)
    {
        var entered = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!entered.IsSuccess)
        {
            return entered.Error!;
        }

        var (cohort, canManage) = entered.Value;
        var now = clock.GetUtcNow();
        var list = await classes.ListAsync(request.CohortId, cancellationToken);
        var attendance = canManage ? await classes.ListAttendanceAsync([.. list.Select(c => c.Id)], cancellationToken) : [];
        var people = await directory.GetAsync([.. attendance.Select(a => a.TraineeId).Distinct()], cancellationToken);

        return new LiveClassList(
            server.IsConfigured,
            canManage,
            new TextDto(cohort.NameAr, cohort.NameEn),
            [
                .. list.Select(c => new LiveClassItem(
                    c.Id.Value,
                    new TextDto(c.Title.Ar, c.Title.En),
                    c.StartsAt,
                    c.DurationMinutes,
                    c.StatusAt(now),
                    canManage && c.CheckCanStart(now).IsSuccess,
                    [
                        .. attendance.Where(a => a.LiveClassId == c.Id && people.ContainsKey(a.TraineeId))
                            .Select(a => new LiveAttendee(new TextDto(people[a.TraineeId].NameAr, people[a.TraineeId].NameEn), a.JoinedAt)),
                    ])),
            ]);
    }
}

public sealed record ScheduleLiveClassCommand(Guid CohortId, string TitleAr, string TitleEn, DateTimeOffset StartsAt, int DurationMinutes) : ICommand<Guid>;

internal sealed class ScheduleLiveClassValidator : AbstractValidator<ScheduleLiveClassCommand>
{
    public ScheduleLiveClassValidator()
    {
        RuleFor(c => c.TitleAr).NotEmpty().MaximumLength(LiveClass.TitleMaxLength);
        RuleFor(c => c.TitleEn).NotEmpty().MaximumLength(LiveClass.TitleMaxLength);
    }
}

internal sealed class ScheduleLiveClassHandler(LiveClassAccess access, ILiveClassRepository classes, ILearningUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<ScheduleLiveClassCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ScheduleLiveClassCommand request, CancellationToken cancellationToken)
    {
        var entered = await access.EnterAsync(request.CohortId, cancellationToken);
        if (!entered.IsSuccess)
        {
            return entered.Error!;
        }

        if (!entered.Value.CanManage)
        {
            return ContentErrors.Forbidden;
        }

        var title = LocalizedText.Create(request.TitleAr, request.TitleEn, LiveClass.TitleMaxLength);
        if (!title.IsSuccess)
        {
            return title.Error!;
        }

        var live = LiveClass.Schedule(request.CohortId, title.Value, request.StartsAt, request.DurationMinutes, clock.GetUtcNow());
        if (!live.IsSuccess)
        {
            return live.Error!;
        }

        classes.Add(live.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return live.Value.Id.Value;
    }
}

/// <summary>A join link is a password for one person, so it is returned to that person only, and never stored.</summary>
public sealed record JoinLink(string Url);

public sealed class LiveClassOpener(LiveClassAccess access, ILiveClassRepository classes)
{
    public async Task<Result<(LiveClass Live, bool CanManage)>> OpenAsync(Guid cohortId, Guid id, CancellationToken cancellationToken)
    {
        var entered = await access.EnterAsync(cohortId, cancellationToken);
        if (!entered.IsSuccess)
        {
            return entered.Error!;
        }

        var live = await classes.FindAsync(new LiveClassId(id), cancellationToken);
        return live is null || live.CohortId != cohortId ? LiveClassErrors.NotFound : (live, entered.Value.CanManage);
    }
}

public sealed record JoinLiveClassCommand(Guid CohortId, Guid Id) : ICommand<JoinLink>;

/// <summary>Staff open the room and enter as moderators. Trainees can enter only once it is open.</summary>
internal sealed class JoinLiveClassHandler(
    LiveClassOpener opener,
    TraineeGate gate,
    ILiveClassRepository classes,
    ILiveClassServer server,
    IUserDirectory directory,
    ICurrentUser user,
    ILearningUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<JoinLiveClassCommand, JoinLink>
{
    public async Task<Result<JoinLink>> Handle(JoinLiveClassCommand request, CancellationToken cancellationToken)
    {
        var opened = await opener.OpenAsync(request.CohortId, request.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        if (!server.IsConfigured)
        {
            return LiveClassErrors.NotConfigured;
        }

        var (live, canManage) = opened.Value;
        var now = clock.GetUtcNow();
        var me = user.Id!.Value;
        var people = await directory.GetAsync([me], cancellationToken);
        var name = people.TryGetValue(me, out var person) ? person.NameAr : "";

        if (canManage)
        {
            var allowed = live.CheckCanStart(now);
            if (!allowed.IsSuccess)
            {
                return allowed.Error!;
            }

            if (!await server.OpenAsync(new LiveMeeting(live.MeetingId, live.Title.Ar, live.DurationMinutes), cancellationToken))
            {
                return LiveClassErrors.Unavailable;
            }

            live.MarkStarted(now);
        }
        else
        {
            var cohort = await gate.EnterAsync(request.CohortId, cancellationToken);
            if (!cohort.IsSuccess)
            {
                return cohort.Error!;
            }

            var allowed = live.CheckCanJoin(now);
            if (!allowed.IsSuccess)
            {
                return allowed.Error!;
            }

            if (!await classes.HasAttendedAsync(live.Id, me, cancellationToken))
            {
                classes.Add(LiveAttendance.Record(live.Id, me, now));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new JoinLink(server.JoinUrl(live.MeetingId, name, me, canManage));
    }
}

public sealed record EndLiveClassCommand(Guid CohortId, Guid Id) : ICommand<Unit>;

internal sealed class EndLiveClassHandler(LiveClassOpener opener, ILiveClassServer server, ILearningUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<EndLiveClassCommand, Unit>
{
    public async Task<Result<Unit>> Handle(EndLiveClassCommand request, CancellationToken cancellationToken)
    {
        var opened = await opener.OpenAsync(request.CohortId, request.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (live, canManage) = opened.Value;
        if (!canManage)
        {
            return ContentErrors.Forbidden;
        }

        // The class closes here even if the server is unreachable, so nobody can join it through this app.
        if (server.IsConfigured)
        {
            await server.CloseAsync(live.MeetingId, cancellationToken);
        }

        live.End(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record CancelLiveClassCommand(Guid CohortId, Guid Id) : ICommand<Unit>;

internal sealed class CancelLiveClassHandler(LiveClassOpener opener, ILiveClassRepository classes, ILearningUnitOfWork unitOfWork) : IRequestHandler<CancelLiveClassCommand, Unit>
{
    public async Task<Result<Unit>> Handle(CancelLiveClassCommand request, CancellationToken cancellationToken)
    {
        var opened = await opener.OpenAsync(request.CohortId, request.Id, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var (live, canManage) = opened.Value;
        if (!canManage)
        {
            return ContentErrors.Forbidden;
        }

        var allowed = live.CheckCanCancel();
        if (!allowed.IsSuccess)
        {
            return allowed.Error!;
        }

        classes.Remove(live);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
