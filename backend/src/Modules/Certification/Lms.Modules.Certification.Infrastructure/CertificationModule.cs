using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Contracts;
using Lms.Modules.Certification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Certification.Infrastructure;

public sealed class CertificationDbContext(DbContextOptions<CertificationDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), ICertificationUnitOfWork
{
    public const string SchemaName = "certification";

    protected override string Schema => SchemaName;

    public DbSet<Certificate> Certificates => Set<Certificate>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<BadgeAward> BadgeAwards => Set<BadgeAward>();

    public DbSet<Competency> Competencies => Set<Competency>();

    public DbSet<CompetencySignOff> CompetencySignOffs => Set<CompetencySignOff>();

    public DbSet<LearningPlan> LearningPlans => Set<LearningPlan>();

    public DbSet<PlanAssignment> PlanAssignments => Set<PlanAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Badge>(b =>
        {
            b.ToTable("Badges");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, value => new BadgeId(value)).ValueGeneratedNever();
            b.Property(x => x.Icon).HasMaxLength(40).IsUnicode(false);
            b.HasIndex(x => x.CourseId);
            b.ComplexProperty(x => x.Name, t =>
            {
                t.Property(y => y.Ar).HasColumnName("NameAr").HasMaxLength(Badge.NameMaxLength);
                t.Property(y => y.En).HasColumnName("NameEn").HasMaxLength(Badge.NameMaxLength);
            });
            b.ComplexProperty(x => x.Description, t =>
            {
                t.Property(y => y.Ar).HasColumnName("DescriptionAr").HasMaxLength(Badge.DescriptionMaxLength);
                t.Property(y => y.En).HasColumnName("DescriptionEn").HasMaxLength(Badge.DescriptionMaxLength);
            });
        });

        modelBuilder.Entity<BadgeAward>(b =>
        {
            b.ToTable("BadgeAwards");
            b.HasKey(x => x.Id);
            b.Property(x => x.BadgeId).HasConversion(id => id.Value, value => new BadgeId(value));
            b.HasIndex(x => new { x.BadgeId, x.TraineeId }).IsUnique();
            b.HasIndex(x => x.TraineeId);
        });

        modelBuilder.Entity<Competency>(b =>
        {
            b.ToTable("Competencies");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, value => new CompetencyId(value)).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(Competency.CodeMaxLength).IsUnicode(false);
            b.HasIndex(x => x.Code).IsUnique();
            b.ComplexProperty(x => x.Name, t =>
            {
                t.Property(y => y.Ar).HasColumnName("NameAr").HasMaxLength(Competency.NameMaxLength);
                t.Property(y => y.En).HasColumnName("NameEn").HasMaxLength(Competency.NameMaxLength);
            });
            b.ComplexProperty(x => x.Description, t =>
            {
                t.Property(y => y.Ar).HasColumnName("DescriptionAr").HasMaxLength(Competency.DescriptionMaxLength);
                t.Property(y => y.En).HasColumnName("DescriptionEn").HasMaxLength(Competency.DescriptionMaxLength);
            });
            b.OwnsMany(x => x.Courses, c =>
            {
                c.ToTable("CompetencyCourses");
                c.WithOwner().HasForeignKey("CompetencyId");
                c.HasKey("CompetencyId", nameof(CompetencyCourse.CourseId));
                c.Property(x => x.CourseId).ValueGeneratedNever();
            });
            b.Navigation(x => x.Courses).HasField("_courses").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CompetencySignOff>(b =>
        {
            b.ToTable("CompetencySignOffs");
            b.HasKey(x => x.Id);
            b.Property(x => x.CompetencyId).HasConversion(id => id.Value, value => new CompetencyId(value));
            b.HasIndex(x => new { x.CompetencyId, x.TraineeId }).IsUnique();
            b.HasIndex(x => x.TraineeId);
        });

        modelBuilder.Entity<LearningPlan>(b =>
        {
            b.ToTable("LearningPlans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, value => new LearningPlanId(value)).ValueGeneratedNever();
            b.ComplexProperty(x => x.Name, t =>
            {
                t.Property(y => y.Ar).HasColumnName("NameAr").HasMaxLength(LearningPlan.NameMaxLength);
                t.Property(y => y.En).HasColumnName("NameEn").HasMaxLength(LearningPlan.NameMaxLength);
            });
            b.ComplexProperty(x => x.Description, t =>
            {
                t.Property(y => y.Ar).HasColumnName("DescriptionAr").HasMaxLength(LearningPlan.DescriptionMaxLength);
                t.Property(y => y.En).HasColumnName("DescriptionEn").HasMaxLength(LearningPlan.DescriptionMaxLength);
            });
            b.OwnsMany(x => x.Items, i =>
            {
                i.ToTable("LearningPlanItems");
                i.WithOwner().HasForeignKey("PlanId");
                i.Property(x => x.CompetencyId).HasConversion(id => id.Value, value => new CompetencyId(value)).ValueGeneratedNever();
                i.HasKey("PlanId", nameof(PlanItem.CompetencyId));
            });
            b.Navigation(x => x.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<PlanAssignment>(b =>
        {
            b.ToTable("PlanAssignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasConversion(id => id.Value, value => new PlanAssignmentId(value)).ValueGeneratedNever();
            b.Property(x => x.PlanId).HasConversion(id => id.Value, value => new LearningPlanId(value));
            b.HasIndex(x => new { x.PlanId, x.TraineeId }).IsUnique();
            b.HasIndex(x => x.TraineeId);
        });

        modelBuilder.Entity<Certificate>(b =>
        {
            b.ToTable("Certificates");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion(id => id.Value, value => new CertificateId(value)).ValueGeneratedNever();
            b.Property(c => c.Code).HasMaxLength(14).IsUnicode(false);
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.TraineeNameAr).HasMaxLength(150);
            b.Property(c => c.TraineeNameEn).HasMaxLength(150);
            b.Property(c => c.CourseCode).HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.CourseTitleAr).HasMaxLength(200);
            b.Property(c => c.CourseTitleEn).HasMaxLength(200);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.RevocationReason).HasMaxLength(Certificate.ReasonMaxLength);
            b.Property<byte[]>("RowVersion").IsRowVersion();
            b.HasIndex(c => c.TraineeId);

            // The database's last line of defence against two valid certificates for one completion.
            b.HasIndex(c => new { c.CohortId, c.TraineeId }).IsUnique().HasFilter("[Status] = 'Issued'");
        });
    }
}

internal sealed class CertificateStats(CertificationDbContext db) : ICertificateStats
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountIssuedAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken)
    {
        var rows = await db.Certificates.AsNoTracking()
            .Where(c => cohortIds.Contains(c.CohortId) && c.Status == CertificateStatus.Issued)
            .GroupBy(c => c.CohortId)
            .Select(g => new { CohortId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.CohortId, r => r.Count);
    }
}

internal sealed class CertificateRepository(CertificationDbContext db) : ICertificateRepository
{
    public Task<Certificate?> FindAsync(CertificateId id, CancellationToken cancellationToken) =>
        db.Certificates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Certificate?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public Task<bool> ExistsForAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken) =>
        db.Certificates.AnyAsync(c => c.CohortId == cohortId && c.TraineeId == traineeId, cancellationToken);

    public async Task<IReadOnlyList<Certificate>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken) =>
        await db.Certificates.AsNoTracking()
            .Where(c => c.TraineeId == traineeId)
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<Certificate>> ListAsync(CertificateFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Certificates.AsNoTracking();

        if (filter.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(c => c.IssuedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(c => c.IssuedAt < end);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(c => c.Code.Contains(term) || c.TraineeNameAr.Contains(term) || c.TraineeNameEn.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.IssuedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Certificate>(items, total, filter.Page, filter.PageSize);
    }

    public void Add(Certificate certificate) => db.Certificates.Add(certificate);
}

public static class CertificationModule
{
    public static IServiceCollection AddCertificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CertificationDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CertificationDbContext.SchemaName)));

        services.AddScoped<ICertificationUnitOfWork>(sp => sp.GetRequiredService<CertificationDbContext>());
        services.AddScoped<ICertificateRepository, CertificateRepository>();
        services.AddScoped<CertificateDetailsReader>();
        services.AddScoped<IBadgeRepository, BadgeRepository>();
        services.AddScoped<BadgeAwarder>();
        services.AddScoped<ICompetencyRepository, CompetencyRepository>();
        services.AddScoped<ILearningPlanRepository, LearningPlanRepository>();
        services.AddHostedService<LearningPlanAssignedProcessor>();
        services.AddScoped<ICertificateIssuer, CertificateIssuer>();
        services.AddSingleton<ICertificatePdf, SkiaCertificatePdf>();
        services.AddHostedService<CertificateIssuedProcessor>();
        services.AddHostedService<CertificateRevokedProcessor>();
        services.AddScoped<ICertificateStats, CertificateStats>();
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, CertificationPersonalData>();
        services.AddModuleApplication(typeof(ICertificateRepository).Assembly);
        return services;
    }
}
