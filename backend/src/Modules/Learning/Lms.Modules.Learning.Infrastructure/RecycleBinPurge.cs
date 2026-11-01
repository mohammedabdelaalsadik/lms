using Lms.BuildingBlocks.Application;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Modules.Learning.Infrastructure;

internal sealed class RecycleBinRepository(LearningDbContext db) : IRecycleBinRepository
{
    public Task<BinItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.BinItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BinItem>> ListAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.BinItems.AsNoTracking().Where(i => i.CourseId == courseId).OrderByDescending(i => i.RemovedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BinItem>> ListRemovedBeforeAsync(DateTimeOffset cutoff, int take, CancellationToken cancellationToken) =>
        await db.BinItems.Where(i => i.RemovedAt < cutoff).OrderBy(i => i.RemovedAt).Take(take).ToListAsync(cancellationToken);

    public void Add(BinItem item) => db.BinItems.Add(item);

    public void Remove(BinItem item) => db.BinItems.Remove(item);
}

/// <summary>Empties expired items. The database row goes first, so a crash can leave a stray file but never a restorable item with no file.</summary>
internal sealed class RecycleBinPurge(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RecycleBinPurge> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Emptying the recycle bin failed. It will run again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var bin = scope.ServiceProvider.GetRequiredService<IRecycleBinRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ILearningUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var expired = await bin.ListRemovedBeforeAsync(clock.GetUtcNow().AddDays(-BinItem.RetentionDays), BatchSize, cancellationToken);
        var files = expired.SelectMany(RecycleBin.FilesOf).ToList();

        foreach (var item in expired)
        {
            bin.Remove(item);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var key in files)
        {
            storage.Delete(key);
        }
    }
}
