using Lms.Modules.Learning.Contracts;
using Lms.Modules.Learning.Domain;

namespace Lms.Modules.Learning.Application;

public sealed class CourseCompletionService(
    ICourseContentRepository contents,
    ICourseProgressRepository progresses,
    IAttendanceRepository attendance,
    ILearningUnitOfWork unitOfWork,
    TimeProvider clock) : ICourseCompletion
{
    public async Task RecordFinalGradeAsync(Guid cohortId, Guid courseId, Guid traineeId, decimal percent, CancellationToken cancellationToken)
    {
        var progress = await progresses.FindAsync(cohortId, traineeId, cancellationToken);

        if (progress is null)
        {
            progress = CourseProgress.Start(cohortId, courseId, traineeId);
            progresses.Add(progress);
        }

        progress.RecordFinalGrade(percent);

        var content = await contents.FindByCourseAsync(courseId, cancellationToken);
        if (content is not null)
        {
            var tallies = await attendance.TallyAsync(cohortId, cancellationToken);
            progress.EvaluateCompletion(content.Rule(), tallies.GetValueOrDefault(traineeId, AttendanceTally.Empty), clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
