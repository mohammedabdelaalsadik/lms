using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Learning.Infrastructure;

internal sealed class PrivateFileRepository(LearningDbContext db) : IPrivateFileRepository
{
    public Task<PrivateFile?> FindAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        db.PrivateFiles.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId, cancellationToken);

    public async Task<IReadOnlyList<PrivateFile>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.PrivateFiles.AsNoTracking().Where(f => f.OwnerId == ownerId).OrderByDescending(f => f.UploadedAt).ToListAsync(cancellationToken);

    public void Add(PrivateFile file) => db.PrivateFiles.Add(file);

    public void Remove(PrivateFile file) => db.PrivateFiles.Remove(file);
}
