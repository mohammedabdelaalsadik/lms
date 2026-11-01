using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Learning.Infrastructure;

/// <summary>Progress and attendance stay after erasure, as training records. Private files go.</summary>
internal sealed class LearningPersonalData(LearningDbContext db, IFileStorage storage) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var progress = await db.Progress.AsNoTracking().Where(p => p.TraineeId == userId).ToListAsync(cancellationToken);
        var answers = await db.FormResponses.AsNoTracking().Where(r => r.TraineeId == userId).Select(r => r.Answers).ToListAsync(cancellationToken);
        var terms = await db.GlossaryEntries.AsNoTracking().Where(e => e.AuthorId == userId).Select(e => new { e.Term, e.Definition, e.IsApproved }).ToListAsync(cancellationToken);
        var files = await db.PrivateFiles.AsNoTracking().Where(f => f.OwnerId == userId).Select(f => new { f.FileName, f.Size, f.UploadedAt }).ToListAsync(cancellationToken);
        var classes = await db.LiveAttendance.AsNoTracking().Where(a => a.TraineeId == userId).Select(a => a.JoinedAt).ToListAsync(cancellationToken);
        var sessions = await db.AttendanceSessions.AsNoTracking().Where(s => s.Records.Any(r => r.TraineeId == userId)).ToListAsync(cancellationToken);

        return new PersonalDataSection("learning", new
        {
            Progress = progress.Select(p => new { p.CohortId, p.CourseId, ActivitiesCompleted = p.Completions.Count, p.FinalGradePercent, p.CompletedAt }),
            FormAnswers = answers,
            LiveClassesJoined = classes,
            PrivateFiles = files,
            GlossaryEntries = terms,
            Attendance = sessions.Select(s => new { s.CohortId, s.Date, Status = s.Records.First(r => r.TraineeId == userId).Status.ToString() }),
        });
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var keys = await db.PrivateFiles.Where(f => f.OwnerId == userId).Select(f => f.StorageKey).ToListAsync(cancellationToken);
        await db.PrivateFiles.Where(f => f.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);

        foreach (var key in keys)
        {
            storage.Delete(key);
        }
    }
}
