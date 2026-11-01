using Lms.BuildingBlocks.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Reports.Application;

public sealed record MonthPoint(int Year, int Month, int Count);

public sealed record CourseStat(string Code, TextDto Title, int Enrolled, int CompletionPercent);

public sealed record RoleCount(string Role, int Count);

public sealed record UserStats(int Total, IReadOnlyList<RoleCount> ByRole, IReadOnlyList<MonthPoint> NewByMonth);

public sealed record DashboardResponse(
    int Enrolled,
    int Completed,
    int CompletionPercent,
    int Certificates,
    decimal? AverageGrade,
    int CohortsUpcoming,
    int CohortsRunning,
    int CohortsFinished,
    IReadOnlyList<MonthPoint> EnrollmentsByMonth,
    IReadOnlyList<CourseStat> TopCourses,
    UserStats? Users);

public static class DashboardMath
{
    public const int Months = 12;

    /// <summary>The last twelve months, oldest first. Months with nothing in them stay in as zero, so a gap shows as a gap.</summary>
    public static IReadOnlyList<MonthPoint> FillMonths(IEnumerable<DateTimeOffset> moments, DateTimeOffset now)
    {
        var counts = moments.GroupBy(m => (m.Year, m.Month)).ToDictionary(g => g.Key, g => g.Count());
        var first = new DateOnly(now.Year, now.Month, 1).AddMonths(1 - Months);

        return
        [
            .. Enumerable.Range(0, Months).Select(i =>
            {
                var month = first.AddMonths(i);
                return new MonthPoint(month.Year, month.Month, counts.GetValueOrDefault((month.Year, month.Month)));
            }),
        ];
    }

    public static int Percent(int part, int whole) => whole == 0 ? 0 : (int)Math.Round(part * 100m / whole);

    /// <summary>Weighted by enrolment, so a large cohort counts for more than a small one.</summary>
    public static decimal? AverageGrade(IEnumerable<(int Enrolled, decimal? Grade)> cohorts)
    {
        var graded = cohorts.Where(c => c.Grade is not null && c.Enrolled > 0).ToList();
        var total = graded.Sum(c => c.Enrolled);
        return total == 0 ? null : Math.Round(graded.Sum(c => c.Grade!.Value * c.Enrolled) / total, 1);
    }
}

public sealed record GetDashboardQuery : IQuery<DashboardResponse>;

/// <summary>Counts only. Instructors see their own cohorts, and only those who may list users see user numbers.</summary>
internal sealed class GetDashboardHandler(
    IRequestHandler<GetOverviewQuery, OverviewResponse> overview,
    ICohortDirectory cohorts,
    IUserDirectory people,
    ICurrentUser user,
    TimeProvider clock) : IRequestHandler<GetDashboardQuery, DashboardResponse>
{
    private const int TopCourses = 6;

    public async Task<Result<DashboardResponse>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var report = await overview.Handle(new GetOverviewQuery(), cancellationToken);
        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var since = new DateOnly(now.Year, now.Month, 1).AddMonths(1 - DashboardMath.Months).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = report.Value.Rows;
        var enrolments = await cohorts.GetEnrollmentDatesAsync([.. rows.Select(r => r.CohortId)], new DateTimeOffset(since), cancellationToken);

        var courses = rows.GroupBy(r => r.CourseCode)
            .Select(g => new CourseStat(g.Key, g.First().Course, g.Sum(r => r.Enrolled), DashboardMath.Percent(g.Sum(r => r.Completed), g.Sum(r => r.Enrolled))))
            .Where(c => c.Enrolled > 0)
            .OrderByDescending(c => c.Enrolled)
            .Take(TopCourses)
            .ToList();

        UserStats? users = null;
        if (user.Role is LmsRoles.Admin or LmsRoles.Auditor)
        {
            var (roles, joined) = await people.GetStatsAsync(new DateTimeOffset(since), cancellationToken);
            users = new UserStats(
                roles.Values.Sum(),
                [.. roles.OrderByDescending(r => r.Value).Select(r => new RoleCount(r.Key, r.Value))],
                DashboardMath.FillMonths(joined, now));
        }

        return new DashboardResponse(
            report.Value.Enrolled,
            report.Value.Completed,
            DashboardMath.Percent(report.Value.Completed, report.Value.Enrolled),
            report.Value.Certificates,
            DashboardMath.AverageGrade(rows.Select(r => (r.Enrolled, r.AverageGrade))),
            rows.Count(r => r.StartsOn > today),
            rows.Count(r => r.StartsOn <= today && r.EndsOn >= today),
            rows.Count(r => r.EndsOn < today),
            DashboardMath.FillMonths(enrolments, now),
            courses,
            users);
    }
}
