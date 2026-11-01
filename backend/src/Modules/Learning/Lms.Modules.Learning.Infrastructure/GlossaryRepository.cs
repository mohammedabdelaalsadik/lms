using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Learning.Infrastructure;

internal sealed class GlossaryRepository(LearningDbContext db) : IGlossaryRepository
{
    public Task<GlossaryEntry?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.GlossaryEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GlossaryEntry>> ListAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.GlossaryEntries.AsNoTracking().Where(e => e.CourseId == courseId).OrderBy(e => e.Term.Ar).ToListAsync(cancellationToken);

    public void Add(GlossaryEntry entry) => db.GlossaryEntries.Add(entry);

    public void Remove(GlossaryEntry entry) => db.GlossaryEntries.Remove(entry);
}
