using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Certification.Contracts;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Learning.Contracts;
using Lms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Lms.Modules.Reports.Application;

public sealed record TextDto(string Ar, string En);

public sealed record CohortReportRow(
    Guid CohortId,
    string CourseCode,
    TextDto Course,
    TextDto Cohort,
    TextDto Instructor,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Enrolled,
    int Completed,
    int CompletionPercent,
    decimal? AverageGrade,
    int Certificates);

public sealed record OverviewResponse(int Cohorts, int Enrolled, int Completed, int Certificates, IReadOnlyList<CohortReportRow> Rows);

public sealed record GetOverviewQuery : IQuery<OverviewResponse>;

/// <summary>Numbers only, drawn through each module's contract. Instructors see their own cohorts.</summary>
internal sealed class GetOverviewHandler(
    ICohortDirectory cohorts,
    ICourseCatalog courses,
    IUserDirectory people,
    ICohortProgressStats progress,
    ICertificateStats certificates,
    ICurrentUser user) : IRequestHandler<GetOverviewQuery, OverviewResponse>
{
    public async Task<Result<OverviewResponse>> Handle(GetOverviewQuery request, CancellationToken cancellationToken)
    {
        var list = await cohorts.GetCohortsAsync(user.Role == LmsRoles.Instructor ? user.Id : null, cancellationToken);
        var ids = list.Select(c => c.Id).ToList();
        var enrolled = await cohorts.GetEnrollmentCountsAsync(ids, cancellationToken);
        var stats = await progress.GetAsync(ids, cancellationToken);
        var issued = await certificates.CountIssuedAsync(ids, cancellationToken);
        var titles = await courses.GetAsync([.. list.Select(c => c.CourseId).Distinct()], cancellationToken);
        var teachers = await people.GetAsync([.. list.Select(c => c.InstructorId).Distinct()], cancellationToken);

        var rows = list.Select(c =>
        {
            var count = enrolled.GetValueOrDefault(c.Id);
            var stat = stats.GetValueOrDefault(c.Id);
            var title = titles.GetValueOrDefault(c.CourseId);
            var teacher = teachers.GetValueOrDefault(c.InstructorId);
            var completed = stat?.Completed ?? 0;

            return new CohortReportRow(
                c.Id,
                title?.Code ?? "",
                new TextDto(title?.TitleAr ?? "", title?.TitleEn ?? ""),
                new TextDto(c.NameAr, c.NameEn),
                new TextDto(teacher?.NameAr ?? "", teacher?.NameEn ?? ""),
                c.StartsOn,
                c.EndsOn,
                count,
                completed,
                count == 0 ? 0 : (int)Math.Round(completed * 100m / count),
                stat?.AverageGrade,
                issued.GetValueOrDefault(c.Id));
        }).ToList();

        return new OverviewResponse(rows.Count, rows.Sum(r => r.Enrolled), rows.Sum(r => r.Completed), rows.Sum(r => r.Certificates), rows);
    }
}

public sealed record ExportOverviewQuery : IQuery<CsvFile>, IAuditedRequest;

internal sealed class ExportOverviewHandler(IRequestHandler<GetOverviewQuery, OverviewResponse> overview) : IRequestHandler<ExportOverviewQuery, CsvFile>
{
    public async Task<Result<CsvFile>> Handle(ExportOverviewQuery request, CancellationToken cancellationToken)
    {
        var report = await overview.Handle(new GetOverviewQuery(), cancellationToken);
        if (!report.IsSuccess)
        {
            return report.Error!;
        }

        IReadOnlyList<string?>[] header =
        [
            ["الرمز / Code", "الدورة / Course", "Course", "الدفعة / Cohort", "Cohort", "المدرب / Instructor", "البداية / Starts", "النهاية / Ends", "المسجلون / Enrolled", "أكملوا / Completed", "نسبة الإكمال / Completion %", "متوسط الدرجة / Average grade", "الشهادات / Certificates"],
        ];

        var rows = report.Value.Rows.Select(r => (IReadOnlyList<string?>)
        [
            r.CourseCode, r.Course.Ar, r.Course.En, r.Cohort.Ar, r.Cohort.En, r.Instructor.Ar,
            r.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.EndsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            N(r.Enrolled), N(r.Completed), N(r.CompletionPercent), r.AverageGrade?.ToString(CultureInfo.InvariantCulture), N(r.Certificates),
        ]);

        return CsvWriter.Write("training-overview.csv", header.Concat(rows));
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}

public static class ReportsModule
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services) =>
        services.AddModuleApplication(typeof(ReportsModule).Assembly);
}
