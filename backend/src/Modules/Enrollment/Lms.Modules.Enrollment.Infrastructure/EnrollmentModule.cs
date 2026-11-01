using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Enrollment.Application;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Enrollment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Enrollment.Infrastructure;

public sealed class EnrollmentDbContext(DbContextOptions<EnrollmentDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), IEnrollmentUnitOfWork
{
    public const string SchemaName = "enrollment";

    protected override string Schema => SchemaName;

    public DbSet<Cohort> Cohorts => Set<Cohort>();

    public DbSet<CohortGroup> Groups => Set<CohortGroup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CohortGroup>(b =>
        {
            b.ToTable("Groups");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).HasConversion(id => id.Value, value => new CohortGroupId(value)).ValueGeneratedNever();
            b.Property(g => g.Name).HasMaxLength(CohortGroup.NameMaxLength);
            b.HasIndex(g => g.CohortId);
            b.OwnsMany(g => g.Members, m =>
            {
                m.ToTable("GroupMembers");
                m.WithOwner().HasForeignKey("GroupId");
                m.Property(x => x.TraineeId).ValueGeneratedNever();
                m.HasKey("GroupId", nameof(GroupMember.TraineeId));

                // The database also holds the one-group-per-cohort rule.
                m.HasIndex(x => new { x.CohortId, x.TraineeId }).IsUnique();
            });
            b.Navigation(g => g.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Cohort>(b =>
        {
            b.ToTable("Cohorts");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new CohortId(value)).ValueGeneratedNever();
            b.Property(c => c.Branch).HasMaxLength(Cohort.BranchMaxLength);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(c => c.CourseId);
            b.HasIndex(c => new { c.Status, c.StartsOn });

            b.ComplexProperty(c => c.Name, n =>
            {
                n.Property(x => x.Ar).HasColumnName("NameAr").HasMaxLength(Cohort.NameMaxLength);
                n.Property(x => x.En).HasColumnName("NameEn").HasMaxLength(Cohort.NameMaxLength);
            });

            b.OwnsMany(c => c.Enrollments, e =>
            {
                e.ToTable("Enrollments");
                e.WithOwner().HasForeignKey("CohortId");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasConversion(id => id.Value, value => new EnrollmentId(value)).ValueGeneratedNever();
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
                e.Property(x => x.RejectionReason).HasMaxLength(CohortEnrollment.ReasonMaxLength);
                e.HasIndex(x => x.TraineeId);

                // The database's last line of defence against a double enrollment.
                e.HasIndex("CohortId", nameof(CohortEnrollment.TraineeId))
                    .IsUnique()
                    .HasFilter("[Status] IN ('Pending', 'Enrolled')");
            });

            b.Navigation(c => c.Enrollments).HasField("_enrollments").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class CohortRepository(EnrollmentDbContext db) : ICohortRepository
{
    public Task<Cohort?> FindAsync(CohortId id, CancellationToken cancellationToken) =>
        db.Cohorts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Cohort cohort) => db.Cohorts.Add(cohort);

    public async Task<PagedResult<CohortRow>> ListAsync(CohortFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Cohorts.AsNoTracking();

        if (filter.OnlyEnrollable)
        {
            query = query.Where(c => c.Status == CohortStatus.Open && c.StartsOn > filter.Today);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(c => c.Name.Ar.Contains(term) || c.Name.En.Contains(term) || c.Branch.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var viewer = filter.ViewerId;

        var items = await query
            .OrderBy(c => c.StartsOn)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new CohortRow(
                c.Id.Value,
                c.CourseId,
                c.Name.Ar,
                c.Name.En,
                c.Branch,
                c.InstructorId,
                c.StartsOn,
                c.EndsOn,
                c.Capacity,
                c.EnrolledCount,
                c.Enrollments.Count(e => e.Status == EnrollmentStatus.Pending),
                c.RequiresApproval,
                c.Status,
                c.Enrollments
                    .Where(e => e.TraineeId == viewer && (e.Status == EnrollmentStatus.Pending || e.Status == EnrollmentStatus.Enrolled))
                    .Select(e => (EnrollmentStatus?)e.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<CohortRow>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<MyEnrollmentRow>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken)
    {
        var cohorts = await db.Cohorts.AsNoTracking()
            .Where(c => c.Enrollments.Any(e => e.TraineeId == traineeId))
            .ToListAsync(cancellationToken);

        return [.. cohorts
            .SelectMany(c => c.Enrollments.Where(e => e.TraineeId == traineeId).Select(e => new MyEnrollmentRow(
                e.Id.Value,
                e.Status,
                e.RejectionReason,
                e.RequestedAt,
                new CohortRow(
                    c.Id.Value,
                    c.CourseId,
                    c.Name.Ar,
                    c.Name.En,
                    c.Branch,
                    c.InstructorId,
                    c.StartsOn,
                    c.EndsOn,
                    c.Capacity,
                    c.EnrolledCount,
                    0,
                    c.RequiresApproval,
                    c.Status,
                    e.Status))))
            .OrderByDescending(r => r.RequestedAt)];
    }
}

internal sealed class CohortGroupRepository(EnrollmentDbContext db) : ICohortGroupRepository
{
    public async Task<IReadOnlyList<CohortGroup>> ListAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.Groups.Where(g => g.CohortId == cohortId).ToListAsync(cancellationToken);

    public void Add(CohortGroup group) => db.Groups.Add(group);

    public void Remove(CohortGroup group) => db.Groups.Remove(group);
}

public static class EnrollmentModule
{
    public static IServiceCollection AddEnrollmentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EnrollmentDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", EnrollmentDbContext.SchemaName)));

        services.AddScoped<IEnrollmentUnitOfWork>(sp => sp.GetRequiredService<EnrollmentDbContext>());
        services.AddScoped<ICohortRepository, CohortRepository>();
        services.AddScoped<ICohortDirectory, CohortDirectory>();
        services.AddScoped<CohortEnricher>();
        services.AddScoped<GroupAccess>();
        services.AddScoped<ICohortGroupRepository, CohortGroupRepository>();
        services.AddHostedService<TraineeEnrolledProcessor>();
        services.AddHostedService<EnrollmentRejectedProcessor>();
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, EnrollmentPersonalData>();
        services.AddModuleApplication(typeof(ICohortRepository).Assembly);
        return services;
    }
}
