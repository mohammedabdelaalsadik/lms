using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.BuildingBlocks.Infrastructure;

/// <summary>
/// Delivers one kind of stored event to another module. A message is marked done only after its
/// handler succeeds, so a failure is retried on the next pass. Handlers must therefore be safe to repeat.
/// </summary>
public abstract class OutboxProcessor<TContext>(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
    where TContext : ModuleDbContext
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected abstract string EventType { get; }

    protected abstract Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Outbox pass failed for {EventType}", EventType);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var messages = await db.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.Type == EventType && m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await using var handlerScope = scopes.CreateAsyncScope();
                await HandleAsync(handlerScope.ServiceProvider, message.Payload, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One bad message must not hold up the rest. It stays unprocessed and is retried.
                logger.LogError(exception, "Outbox message {MessageId} failed", message.Id);
                continue;
            }

            await db.Set<OutboxMessage>()
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow), cancellationToken);
        }
    }
}
