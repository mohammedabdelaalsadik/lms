using Lms.Modules.Reports.Application;

namespace Lms.UnitTests;

public class DashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void There_are_always_twelve_months_ending_this_month()
    {
        var months = DashboardMath.FillMonths([], Now);

        Assert.Equal(12, months.Count);
        Assert.Equal((2025, 12), (months[0].Year, months[0].Month));
        Assert.Equal((2026, 11), (months[^1].Year, months[^1].Month));
    }

    [Fact]
    public void Each_moment_lands_in_its_month()
    {
        var months = DashboardMath.FillMonths([Now, Now.AddDays(1), Now.AddMonths(-2)], Now);

        Assert.Equal(2, months[^1].Count);
        Assert.Equal(1, months[^3].Count);
        Assert.Equal(0, months[^2].Count);
    }

    [Fact]
    public void Older_moments_are_left_out() => Assert.All(DashboardMath.FillMonths([Now.AddYears(-2)], Now), m => Assert.Equal(0, m.Count));

    [Fact]
    public void Nothing_enrolled_is_zero_percent_not_a_crash() => Assert.Equal(0, DashboardMath.Percent(0, 0));

    [Fact]
    public void Percent_rounds() => Assert.Equal(67, DashboardMath.Percent(2, 3));

    [Fact]
    public void Larger_cohorts_count_for_more_in_the_average() =>
        Assert.Equal(82.5m, DashboardMath.AverageGrade([(30, 90m), (10, 60m)]));

    [Fact]
    public void Cohorts_without_grades_stay_out_of_the_average() =>
        Assert.Equal(80m, DashboardMath.AverageGrade([(10, 80m), (50, null)]));

    [Fact]
    public void No_grades_means_no_average() => Assert.Null(DashboardMath.AverageGrade([(10, null)]));
}
