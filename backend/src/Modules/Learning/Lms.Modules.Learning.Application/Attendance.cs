using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Learning.Application;

public sealed record AttendanceEntry(Guid TraineeId, AttendanceStatus Status);

public sealed record AttendanceTrainee(Guid Id, TextDto Name, string MaskedNationalId);

public sealed record AttendanceSheet(Guid Id, DateOnly Date, IReadOnlyList<AttendanceEntry> Entries);

public sealed record CohortAttendanceResponse(
    DateOnly StartsOn,
    DateOnly EndsOn,
    IReadOnlyList<AttendanceTrainee> Trainees,
    IReadOnlyList<AttendanceSheet> Sessions);

public sealed record GetCohortAttendanceQuery(Guid CohortId) : IQuery<CohortAttendanceResponse>;

internal sealed class GetCohortAttendanceHandler(
    ICohortDirectory cohorts,
    LearningAccess access,
    IAttendanceRepository attendance,
    IUserDirectory directory) : IRequestHandler<GetCohortAttendanceQuery, CohortAttendanceResponse>
{
    public async Task<Result<CohortAttendanceResponse>> Handle(GetCohortAttendanceQuery request, CancellationToken cancellationToken)
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

        var ids = await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken);
        var names = await directory.GetAsync(ids, cancellationToken);
        var sessions = await attendance.ListByCohortAsync(request.CohortId, cancellationToken);

        return new CohortAttendanceResponse(
            cohort.StartsOn,
            cohort.EndsOn,
            [.. ids.Select(id =>
            {
                var name = names.GetValueOrDefault(id);
                return new AttendanceTrainee(id, new TextDto(name?.NameAr ?? "", name?.NameEn ?? ""), name?.MaskedNationalId ?? "");
            })],
            [.. sessions.OrderByDescending(s => s.Date).Select(s => new AttendanceSheet(
                s.Id.Value,
                s.Date,
                [.. s.Records.Select(r => new AttendanceEntry(r.TraineeId, r.Status))]))]);
    }
}

public sealed record SaveAttendanceCommand(Guid CohortId, DateOnly Date, IReadOnlyList<AttendanceEntry> Entries) : ICommand<Unit>;

internal sealed class SaveAttendanceValidator : AbstractValidator<SaveAttendanceCommand>
{
    public SaveAttendanceValidator()
    {
        RuleFor(c => c.Entries).NotEmpty();
        RuleForEach(c => c.Entries).ChildRules(e => e.RuleFor(x => x.Status).IsInEnum());
    }
}

internal sealed class SaveAttendanceHandler(
    ICohortDirectory cohorts,
    LearningAccess access,
    ICurrentUser user,
    IAttendanceRepository attendance,
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    ILearningUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SaveAttendanceCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SaveAttendanceCommand request, CancellationToken cancellationToken)
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

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (request.Date < cohort.StartsOn || request.Date > cohort.EndsOn || request.Date > today)
        {
            return ProgressErrors.InvalidSessionDate;
        }

        var enrolled = (await cohorts.GetEnrolledTraineesAsync(request.CohortId, cancellationToken)).ToHashSet();

        if (!request.Entries.All(e => enrolled.Contains(e.TraineeId)))
        {
            return ProgressErrors.TraineeNotInCohort;
        }

        var entries = request.Entries.GroupBy(e => e.TraineeId).ToDictionary(g => g.Key, g => g.Last().Status);
        var session = await attendance.FindAsync(request.CohortId, request.Date, cancellationToken);

        if (session is null)
        {
            attendance.Add(AttendanceSession.Take(request.CohortId, request.Date, user.Id!.Value, entries));
        }
        else
        {
            session.Correct(entries, user.Id!.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await EvaluateAsync(cohort, entries.Keys, now, cancellationToken);
        return default(Unit);
    }

    /// <summary>Attendance alone can finish a course, so every affected trainee is checked.</summary>
    private async Task EvaluateAsync(CohortInfo cohort, IEnumerable<Guid> trainees, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var content = await contents.FindByCourseAsync(cohort.CourseId, cancellationToken);
        if (content is null)
        {
            return;
        }

        var rule = content.Rule();
        var tallies = await attendance.TallyAsync(cohort.Id, cancellationToken);

        foreach (var traineeId in trainees)
        {
            var progress = await progresses.FindAsync(cohort.Id, traineeId, cancellationToken);

            if (progress is null)
            {
                progress = CourseProgress.Start(cohort.Id, cohort.CourseId, traineeId);
                progresses.Add(progress);
            }

            progress.EvaluateCompletion(rule, tallies.GetValueOrDefault(traineeId, AttendanceTally.Empty), now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
