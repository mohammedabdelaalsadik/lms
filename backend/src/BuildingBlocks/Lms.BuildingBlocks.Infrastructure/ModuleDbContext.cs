using Lms.BuildingBlocks.Application;
using Lms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lms.BuildingBlocks.Infrastructure;

/// <summary>Domain event stored in the same transaction as the change that raised it.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

/// <summary>One context per module, each in its own SQL schema, with no cross-module foreign keys.</summary>
public abstract class ModuleDbContext(DbContextOptions options, TimeProvider clock) : DbContext(options), IUnitOfWork
{
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.Property(m => m.Type).HasMaxLength(300);
            b.HasIndex(m => m.ProcessedAt).HasFilter("[ProcessedAt] IS NULL");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).ToList();
        var now = clock.GetUtcNow();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var type = domainEvent.GetType();
                Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    OccurredAt = now,
                    Type = type.FullName!,
                    Payload = JsonSerializer.Serialize(domainEvent, type),
                });
            }

            aggregate.ClearDomainEvents();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
