using Lms.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Modules.Assessment.Infrastructure;

/// <summary>
/// Results stay after erasure, as training records. Only published grades leave here: attempt scores
/// are left out, because an unpublished grade must stay hidden, even from its owner.
/// </summary>
internal sealed class AssessmentPersonalData(AssessmentDbContext db) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.TraineeId == userId).ToListAsync(cancellationToken);
        var quizIds = attempts.Select(a => a.QuizId).Distinct().ToList();
        var quizzes = await db.Quizzes.AsNoTracking().Where(q => quizIds.Contains(q.Id)).Select(q => new { q.Id, q.Title }).ToListAsync(cancellationToken);
        var submissions = await db.Submissions.AsNoTracking().Where(s => s.TraineeId == userId).ToListAsync(cancellationToken);
        var books = await db.Gradebooks.AsNoTracking().Where(g => g.Grades.Any(x => x.TraineeId == userId)).ToListAsync(cancellationToken);

        var written = await db.WorkshopSubmissions.AsNoTracking().Where(s => s.TraineeId == userId).Select(s => new { s.Text, s.SubmittedAt }).ToListAsync(cancellationToken);
        var given = await db.WorkshopReviews.AsNoTracking().Where(r => r.ReviewerId == userId && r.ReviewedAt != null).Select(r => new { r.Score, r.Comment, r.ReviewedAt }).ToListAsync(cancellationToken);

        return new PersonalDataSection("assessment", new
        {
            WorkshopSubmissions = written,
            WorkshopReviewsGiven = given,
            QuizAttempts = attempts.Select(a => new
            {
                Quiz = quizzes.FirstOrDefault(q => q.Id == a.QuizId)?.Title,
                a.Number,
                a.StartedAt,
                a.SubmittedAt,
                Answers = a.Answers.Select(x => x.Response),
            }),
            Submissions = submissions.Select(s => new { s.FileName, s.SubmittedAt, s.IsLate, Feedback = s.ReviewedAt is null ? null : s.Feedback, s.ReviewedAt }),
            PublishedGrades = books.SelectMany(b => b.Grades.Where(x => x.TraineeId == userId).SelectMany(x =>
                b.Items.Where(i => i.Id == x.ItemId && i.PublishedAt != null).Select(i => new { Item = new { i.TitleAr, i.TitleEn }, x.Percent }))),
        });
    }
}
