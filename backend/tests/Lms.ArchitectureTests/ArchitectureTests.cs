using Lms.Modules.Assessment.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Assessment.Infrastructure;
using Lms.Modules.Assessment.Presentation;
using Lms.Modules.Audit.Application;
using Lms.Modules.Audit.Domain;
using Lms.Modules.Audit.Infrastructure;
using Lms.Modules.Audit.Presentation;
using Lms.Modules.Catalog.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Catalog.Domain;
using Lms.Modules.Catalog.Infrastructure;
using Lms.Modules.Catalog.Presentation;
using Lms.Modules.Community.Application;
using Lms.Modules.Community.Domain;
using Lms.Modules.Community.Infrastructure;
using Lms.Modules.Community.Presentation;
using Lms.Modules.Enrollment.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Enrollment.Domain;
using Lms.Modules.Enrollment.Infrastructure;
using Lms.Modules.Enrollment.Presentation;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Identity.Domain;
using Lms.Modules.Identity.Infrastructure;
using Lms.Modules.Identity.Presentation;
using Microsoft.AspNetCore.Mvc;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Contracts;
using Lms.Modules.Learning.Domain;
using Lms.Modules.Learning.Infrastructure;
using Lms.Modules.Learning.Presentation;
using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Contracts;
using Lms.Modules.Certification.Domain;
using Lms.Modules.Certification.Infrastructure;
using Lms.Modules.Certification.Presentation;
using Lms.Modules.Notifications.Application;
using Lms.Modules.Notifications.Contracts;
using Lms.Modules.Notifications.Domain;
using Lms.Modules.Notifications.Infrastructure;
using Lms.Modules.Notifications.Presentation;
using NetArchTest.Rules;
using System.Reflection;

namespace Lms.ArchitectureTests;

public class ArchitectureTests
{
    private const string CatalogRoot = "Lms.Modules.Catalog";
    private const string IdentityRoot = "Lms.Modules.Identity";
    private const string EnrollmentRoot = "Lms.Modules.Enrollment";
    private const string LearningRoot = "Lms.Modules.Learning";
    private const string AssessmentRoot = "Lms.Modules.Assessment";
    private const string CertificationRoot = "Lms.Modules.Certification";

    private static readonly Assembly CatalogDomain = typeof(Course).Assembly;
    private static readonly Assembly CatalogApplication = typeof(ICourseRepository).Assembly;
    private static readonly Assembly CatalogInfrastructure = typeof(CatalogDbContext).Assembly;
    private static readonly Assembly CatalogPresentation = typeof(CoursesController).Assembly;
    private static readonly Assembly EnrollmentDomain = typeof(Cohort).Assembly;
    private static readonly Assembly EnrollmentApplication = typeof(ICohortRepository).Assembly;
    private static readonly Assembly EnrollmentInfrastructure = typeof(EnrollmentDbContext).Assembly;
    private static readonly Assembly EnrollmentPresentation = typeof(CohortsController).Assembly;
    private static readonly Assembly LearningDomain = typeof(CourseContent).Assembly;
    private static readonly Assembly LearningApplication = typeof(ICourseContentRepository).Assembly;
    private static readonly Assembly LearningInfrastructure = typeof(LearningDbContext).Assembly;
    private static readonly Assembly LearningPresentation = typeof(LearningController).Assembly;
    private static readonly Assembly AssessmentDomain = typeof(Gradebook).Assembly;
    private static readonly Assembly AssessmentApplication = typeof(IGradebookRepository).Assembly;
    private static readonly Assembly AssessmentInfrastructure = typeof(AssessmentDbContext).Assembly;
    private static readonly Assembly AssessmentPresentation = typeof(AssessmentStaffController).Assembly;
    private static readonly Assembly CertificationDomain = typeof(Certificate).Assembly;
    private static readonly Assembly CertificationApplication = typeof(ICertificateRepository).Assembly;
    private static readonly Assembly CertificationInfrastructure = typeof(CertificationDbContext).Assembly;
    private static readonly Assembly CertificationPresentation = typeof(CertificatesController).Assembly;
    private static readonly Assembly IdentityDomain = typeof(User).Assembly;
    private static readonly Assembly IdentityApplication = typeof(IUserRepository).Assembly;
    private static readonly Assembly IdentityInfrastructure = typeof(IdentityDbContext).Assembly;
    private static readonly Assembly IdentityPresentation = typeof(AuthController).Assembly;

    public static TheoryData<string, string[]> DomainAssemblies() => new()
    {
        { CatalogRoot, [".Application", ".Infrastructure", ".Presentation"] },
        { IdentityRoot, [".Application", ".Infrastructure", ".Presentation"] },
    };

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_depends_on_no_outer_layer(string root, string[] layers)
    {
        var assembly = root == CatalogRoot ? CatalogDomain : IdentityDomain;

        AssertNoDependency(assembly, [.. layers.Select(l => root + l)]);
    }

    [Fact]
    public void Domain_is_free_of_frameworks()
    {
        string[] frameworks = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "FluentValidation", "Lms.BuildingBlocks"];

        AssertNoDependency(CatalogDomain, frameworks);
        AssertNoDependency(IdentityDomain, frameworks);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web()
    {
        AssertNoDependency(CatalogApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{CatalogRoot}.Infrastructure", $"{CatalogRoot}.Presentation"]);
        AssertNoDependency(IdentityApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{IdentityRoot}.Infrastructure", $"{IdentityRoot}.Presentation"]);
    }

    [Fact]
    public void Presentation_does_not_touch_infrastructure()
    {
        AssertNoDependency(CatalogPresentation, [$"{CatalogRoot}.Infrastructure", "Microsoft.EntityFrameworkCore"]);
        AssertNoDependency(IdentityPresentation, [$"{IdentityRoot}.Infrastructure", "Microsoft.EntityFrameworkCore"]);
    }

    [Fact]
    public void Learning_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot)];

        AssertNoDependency(LearningDomain, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(LearningApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{LearningRoot}.Infrastructure", $"{LearningRoot}.Presentation", .. otherModules]);
        AssertNoDependency(LearningInfrastructure, otherModules);
        AssertNoDependency(LearningPresentation, [$"{LearningRoot}.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
        AssertNoDependency(EnrollmentApplication, [.. Internals(LearningRoot)]);
        AssertNoDependency(typeof(ICohortDirectory).Assembly, [.. Internals(EnrollmentRoot)]);
    }

    [Fact]
    public void Assessment_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot)];

        AssertNoDependency(AssessmentDomain, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(AssessmentApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{AssessmentRoot}.Infrastructure", $"{AssessmentRoot}.Presentation", .. otherModules]);
        AssertNoDependency(AssessmentInfrastructure, otherModules);
        AssertNoDependency(AssessmentPresentation, [$"{AssessmentRoot}.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
        AssertNoDependency(LearningApplication, [.. Internals(AssessmentRoot)]);
        AssertNoDependency(typeof(ICourseCompletion).Assembly, [.. Internals(LearningRoot)]);
    }

    [Fact]
    public void Certification_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot), .. Internals(AssessmentRoot)];

        AssertNoDependency(CertificationDomain, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "SkiaSharp", "QRCoder", .. otherModules]);
        AssertNoDependency(CertificationApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "SkiaSharp", $"{CertificationRoot}.Infrastructure", $"{CertificationRoot}.Presentation", .. otherModules]);
        AssertNoDependency(CertificationInfrastructure, otherModules);
        AssertNoDependency(CertificationPresentation, [$"{CertificationRoot}.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
        AssertNoDependency(LearningInfrastructure, [.. Internals(CertificationRoot)]);
        AssertNoDependency(typeof(ICertificateIssuer).Assembly, [.. Internals(CertificationRoot)]);
    }

    [Fact]
    public void Audit_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot), .. Internals(AssessmentRoot), .. Internals(CertificationRoot)];

        AssertNoDependency(typeof(AuditEntry).Assembly, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(typeof(IAuditRepository).Assembly, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(typeof(AuditDbContext).Assembly, otherModules);
        AssertNoDependency(typeof(AuditController).Assembly, ["Lms.Modules.Audit.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
    }

    [Fact]
    public void Notifications_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot), .. Internals(AssessmentRoot), .. Internals(CertificationRoot)];
        const string Root = "Lms.Modules.Notifications";

        AssertNoDependency(typeof(Notification).Assembly, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(typeof(INotificationRepository).Assembly, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Net.Mail", .. otherModules]);
        AssertNoDependency(typeof(NotificationsDbContext).Assembly, otherModules);
        AssertNoDependency(typeof(NotificationsController).Assembly, [$"{Root}.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
        AssertNoDependency(typeof(INotifier).Assembly, [.. Internals(Root)]);
        AssertNoDependency(EnrollmentInfrastructure, [.. Internals(Root)]);
        AssertNoDependency(CertificationInfrastructure, [.. Internals(Root)]);
    }

    [Fact]
    public void Community_layers_point_inward_and_use_only_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot), .. Internals(AssessmentRoot), .. Internals(CertificationRoot), .. Internals("Lms.Modules.Notifications")];

        AssertNoDependency(typeof(ForumTopic).Assembly, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(typeof(IForumRepository).Assembly, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
        AssertNoDependency(typeof(CommunityDbContext).Assembly, otherModules);
        AssertNoDependency(typeof(ForumsController).Assembly, ["Lms.Modules.Community.Infrastructure", "Microsoft.EntityFrameworkCore", .. otherModules]);
    }

    [Fact]
    public void Reports_read_other_modules_only_through_contracts()
    {
        string[] otherModules = [.. Internals(CatalogRoot), .. Internals(IdentityRoot), .. Internals(EnrollmentRoot), .. Internals(LearningRoot), .. Internals(AssessmentRoot), .. Internals(CertificationRoot)];

        AssertNoDependency(typeof(Lms.Modules.Reports.Application.ReportsModule).Assembly, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", .. otherModules]);
    }

    [Fact]
    public void Modules_talk_only_through_contracts()
    {
        foreach (var assembly in new[] { CatalogDomain, CatalogApplication, CatalogInfrastructure, CatalogPresentation })
        {
            AssertNoDependency(assembly, [.. Internals(IdentityRoot), .. Internals(EnrollmentRoot)]);
        }

        foreach (var assembly in new[] { IdentityDomain, IdentityApplication, IdentityInfrastructure, IdentityPresentation })
        {
            AssertNoDependency(assembly, [.. Internals(CatalogRoot), .. Internals(EnrollmentRoot)]);
        }

        foreach (var assembly in new[] { EnrollmentDomain, EnrollmentApplication, EnrollmentInfrastructure, EnrollmentPresentation })
        {
            AssertNoDependency(assembly, [.. Internals(CatalogRoot), .. Internals(IdentityRoot)]);
        }
    }

    [Fact]
    public void Enrollment_layers_point_inward()
    {
        AssertNoDependency(EnrollmentDomain, ["Lms.BuildingBlocks", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{CatalogRoot}.Contracts", $"{IdentityRoot}.Contracts"]);
        AssertNoDependency(EnrollmentApplication, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", $"{EnrollmentRoot}.Infrastructure", $"{EnrollmentRoot}.Presentation"]);
        AssertNoDependency(EnrollmentPresentation, [$"{EnrollmentRoot}.Infrastructure", "Microsoft.EntityFrameworkCore"]);
    }

    [Fact]
    public void Contracts_expose_no_module_internals()
    {
        AssertNoDependency(typeof(ICourseCatalog).Assembly, [.. Internals(CatalogRoot)]);
        AssertNoDependency(typeof(IUserDirectory).Assembly, [.. Internals(IdentityRoot)]);
    }

    [Fact]
    public void Http_endpoints_are_controllers()
    {
        var controllers = typeof(CoursesController).Assembly.GetTypes()
            .Concat(typeof(AuthController).Assembly.GetTypes())
            .Concat(typeof(CohortsController).Assembly.GetTypes())
            .Concat(typeof(LearningController).Assembly.GetTypes())
            .Concat(typeof(AssessmentStaffController).Assembly.GetTypes())
            .Concat(typeof(CertificatesController).Assembly.GetTypes())
            .Concat(typeof(AuditController).Assembly.GetTypes())
            .Concat(typeof(NotificationsController).Assembly.GetTypes())
            .Concat(typeof(ForumsController).Assembly.GetTypes())
            .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal));

        Assert.All(controllers, c => Assert.True(typeof(ControllerBase).IsAssignableFrom(c), c.Name));
    }

    private static string[] Internals(string root) =>
        [$"{root}.Domain", $"{root}.Application", $"{root}.Infrastructure", $"{root}.Presentation"];

    private static void AssertNoDependency(Assembly assembly, string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
