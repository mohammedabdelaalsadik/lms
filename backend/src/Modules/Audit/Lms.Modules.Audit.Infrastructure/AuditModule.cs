using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Audit.Application;
using Lms.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Audit.Infrastructure;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, TimeProvider clock) : ModuleDbContext(options, clock)
{
    public const string SchemaName = "audit";

    protected override string Schema => SchemaName;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("AuditEntries", t => t.HasTrigger("TR_AuditEntries_AppendOnly"));
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.ActorRole).HasMaxLength(20).IsUnicode(false);
            b.Property(e => e.IpAddress).HasMaxLength(45).IsUnicode(false);
            b.Property(e => e.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsUnicode(false);
            b.Property(e => e.Details).HasMaxLength(AuditEntry.DetailsMaxLength);
            b.Property(e => e.ErrorCode).HasMaxLength(100).IsUnicode(false);
            b.HasIndex(e => e.OccurredAt);
            b.HasIndex(e => new { e.ActorId, e.OccurredAt });
            b.HasIndex(e => new { e.Action, e.OccurredAt });
        });
    }
}

internal sealed class AuditRepository(AuditDbContext db) : IAuditRepository
{
    public void Add(AuditEntry entry) => db.Entries.Add(entry);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<PagedResult<AuditEntry>> ListAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        var query = db.Entries.AsNoTracking();

        if (filter.ActorId is { } actor)
        {
            query = query.Where(e => e.ActorId == actor);
        }

        if (filter.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(e => e.OccurredAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(e => e.OccurredAt < end);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e => e.Action.Contains(term) || e.Details.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntry>(items, total, filter.Page, filter.PageSize);
    }
}

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.SchemaName)));

        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddModuleApplication(typeof(IAuditRepository).Assembly);
        return services;
    }
}
