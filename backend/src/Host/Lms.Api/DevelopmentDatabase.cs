using Lms.Modules.Assessment.Infrastructure;
using Lms.Modules.Audit.Infrastructure;
using Lms.Modules.Catalog.Infrastructure;
using Lms.Modules.Certification.Infrastructure;
using Lms.Modules.Enrollment.Infrastructure;
using Lms.Modules.Community.Infrastructure;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Domain;
using Lms.Modules.Identity.Infrastructure;
using Lms.Modules.Learning.Infrastructure;
using Lms.Modules.Notifications.Infrastructure;
using Lms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api;

internal static class DevelopmentDatabase
{
    /// <summary>Made-up people and IDs, one for each role, for trying the system locally.</summary>
    private static readonly (string NationalId, LocalizedText Name, string Email, Role Role)[] DemoUsers =
    [
        ("1000000016", new LocalizedText("نورة المنسقة", "Noura Coordinator"), "coordinator@lms.local", Role.Coordinator),
        ("1000000024", new LocalizedText("خالد المدرب", "Khalid Instructor"), "instructor@lms.local", Role.Instructor),
        ("1000000032", new LocalizedText("فهد المدقق", "Fahad Auditor"), "auditor@lms.local", Role.Auditor),
        ("2000000006", new LocalizedText("سارة المتدربة", "Sara Trainee"), "trainee1@lms.local", Role.Trainee),
        ("2000000014", new LocalizedText("عمر المتدرب", "Omar Trainee"), "trainee2@lms.local", Role.Trainee),
    ];

    /// <summary>Applies migrations and loads made-up sample data. Development only.</summary>
    public static async Task PrepareDevelopmentDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<TimeProvider>();

        var identity = services.GetRequiredService<IdentityDbContext>();
        var catalog = services.GetRequiredService<CatalogDbContext>();
        await identity.Database.MigrateAsync();
        await catalog.Database.MigrateAsync();
        await services.GetRequiredService<EnrollmentDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<LearningDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<AssessmentDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CertificationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CommunityDbContext>().Database.MigrateAsync();

        await catalog.SeedDevelopmentDataAsync(clock);

        var nationalId = app.Configuration["Seed:AdminNationalId"];
        var password = app.Configuration["Seed:AdminPassword"];

        var protector = services.GetRequiredService<INationalIdProtector>();
        var passwords = services.GetRequiredService<IPasswordService>();

        if (!string.IsNullOrEmpty(nationalId) && !string.IsNullOrEmpty(password))
        {
            await identity.SeedDevelopmentUserAsync(
                protector, passwords, clock, nationalId, password,
                new LocalizedText("مدير النظام", "System Admin"), "admin@lms.local", Role.Admin);
        }

        if (app.Configuration["Seed:DemoPassword"] is { Length: > 0 } demoPassword)
        {
            foreach (var demo in DemoUsers)
            {
                await identity.SeedDevelopmentUserAsync(protector, passwords, clock, demo.NationalId, demoPassword, demo.Name, demo.Email, demo.Role);
            }
        }
    }
}
