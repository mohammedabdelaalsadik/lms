using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

/// <summary>Whether a request came through the exam browser the quiz was set up for.</summary>
public interface IExamClient
{
    string RequestUrl { get; }

    string? ConfigKeyHash { get; }
}

public sealed class ExamGate(IExamClient client)
{
    public bool Allows(Quiz quiz) => quiz.ExamKey is null || SafeExam.Matches(client.RequestUrl, quiz.ExamKey, client.ConfigKeyHash);
}

public enum TimelineKind { Quiz, Assignment, WorkshopSubmission, WorkshopReview, LiveClass }

public sealed record TimelineItem(TimelineKind Kind, TextDto Title, TextDto Cohort, DateTimeOffset? DueAt, bool IsOverdue, string Link);

public sealed record GetMyTimelineQuery : IQuery<IReadOnlyList<TimelineItem>>;

/// <summary>What a trainee still has to do across running cohorts. Finished work drops off, and a closed quiz is no longer a task.</summary>
internal sealed class GetMyTimelineHandler(
    ICohortDirectory cohorts,
    IQuizRepository quizzes,
    IAttemptRepository attempts,
    IAssignmentRepository assignments,
    IWorkshopRepository workshops,
    ILiveClassSchedule live,
    ICurrentUser user,
    TimeProvider clock) : IRequestHandler<GetMyTimelineQuery, IReadOnlyList<TimelineItem>>
{
    public async Task<Result<IReadOnlyList<TimelineItem>>> Handle(GetMyTimelineQuery request, CancellationToken cancellationToken)
    {
        var me = user.Id!.Value;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var items = new List<TimelineItem>();

        foreach (var cohort in (await cohorts.GetEnrolledCohortsAsync(me, cancellationToken)).Where(c => c.StartsOn <= today && c.EndsOn >= today))
        {
            var name = new TextDto(cohort.NameAr, cohort.NameEn);
            var learn = $"/learn/{cohort.Id}";

            var quizList = (await quizzes.ListByCohortAsync(cohort.Id, cancellationToken)).Where(q => q.Status == QuizStatus.Published && q.IsOpen(now)).ToList();
            var taken = (await attempts.ListForTraineeAsync([.. quizList.Select(q => q.Id)], me, cancellationToken)).Where(a => a.IsFinished).Select(a => a.QuizId).ToHashSet();
            items.AddRange(quizList.Where(q => !taken.Contains(q.Id)).Select(q =>
                new TimelineItem(TimelineKind.Quiz, new TextDto(q.Title.Ar, q.Title.En), name, q.ClosesAt, false, $"{learn}/assessments")));

            foreach (var assignment in await assignments.ListByCohortAsync(cohort.Id, cancellationToken))
            {
                if (await assignments.FindSubmissionAsync(assignment.Id, me, cancellationToken) is null)
                {
                    items.Add(new TimelineItem(TimelineKind.Assignment, new TextDto(assignment.Title.Ar, assignment.Title.En), name, assignment.DueAt, assignment.DueAt < now, $"{learn}/assessments"));
                }
            }

            items.AddRange((await live.ListOpenAsync(cohort.Id, cancellationToken)).Select(c =>
                new TimelineItem(TimelineKind.LiveClass, new TextDto(c.TitleAr, c.TitleEn), name, c.StartsAt, false, $"/live/{cohort.Id}")));

            var list = await workshops.ListAsync(cohort.Id, cancellationToken);
            List<WorkshopId> ids = [.. list.Select(w => w.Id)];
            var submissions = await workshops.ListSubmissionsAsync(ids, cancellationToken);
            var reviews = await workshops.ListReviewsAsync(ids, cancellationToken);

            foreach (var workshop in list)
            {
                var title = new TextDto(workshop.Title.Ar, workshop.Title.En);

                if (workshop.Status == WorkshopStatus.Submission && !submissions.Any(s => s.WorkshopId == workshop.Id && s.TraineeId == me))
                {
                    items.Add(new TimelineItem(TimelineKind.WorkshopSubmission, title, name, null, false, $"{learn}/workshops"));
                }

                if (workshop.Status == WorkshopStatus.Review && reviews.Any(r => r.WorkshopId == workshop.Id && r.ReviewerId == me && r.Score is null))
                {
                    items.Add(new TimelineItem(TimelineKind.WorkshopReview, title, name, null, false, $"{learn}/workshops"));
                }
            }
        }

        IReadOnlyList<TimelineItem> ordered = [.. items.OrderByDescending(i => i.IsOverdue).ThenBy(i => i.DueAt ?? DateTimeOffset.MaxValue)];
        return Result<IReadOnlyList<TimelineItem>>.Success(ordered);
    }
}
