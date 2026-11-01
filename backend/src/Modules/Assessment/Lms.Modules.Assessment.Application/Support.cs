using Lms.BuildingBlocks.Application;
using Lms.Modules.Assessment.Domain;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Assessment.Application;

public interface IAssessmentUnitOfWork : IUnitOfWork;

public interface IQuizRepository
{
    Task<Quiz?> FindAsync(QuizId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Quiz>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken);

    void Add(Quiz quiz);
}

public interface IAttemptRepository
{
    Task<QuizAttempt?> FindAsync(AttemptId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuizAttempt>> ListAsync(QuizId quizId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuizAttempt>> ListOpenByQuizAsync(QuizId quizId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuizAttempt>> ListAwaitingMarkingAsync(QuizId quizId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuizAttempt>> ListForTraineeAsync(IReadOnlyCollection<QuizId> quizIds, Guid traineeId, CancellationToken cancellationToken);

    void Add(QuizAttempt attempt);
}

public interface IAssignmentRepository
{
    Task<Assignment?> FindAsync(AssignmentId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Assignment>> ListByCohortAsync(Guid cohortId, CancellationToken cancellationToken);

    Task<AssignmentSubmission?> FindSubmissionAsync(SubmissionId id, CancellationToken cancellationToken);

    Task<AssignmentSubmission?> FindSubmissionAsync(AssignmentId assignmentId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentSubmission>> ListSubmissionsAsync(AssignmentId assignmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentSubmission>> ListSubmissionsForTraineeAsync(IReadOnlyCollection<AssignmentId> ids, Guid traineeId, CancellationToken cancellationToken);

    void Add(Assignment assignment);

    void Add(AssignmentSubmission submission);
}

public interface IGradebookRepository
{
    Task<Gradebook?> FindAsync(Guid cohortId, CancellationToken cancellationToken);

    void Add(Gradebook gradebook);
}

public sealed record TextDto(string Ar, string En);

/// <summary>Who may do what in a cohort. Instructors are limited to the cohorts they teach.</summary>
public sealed class AssessmentAccess(ICohortDirectory cohorts, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<CohortInfo>> StaffAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(cohortId, cancellationToken);
        if (cohort is null)
        {
            return AssessmentErrors.CohortNotFound;
        }

        var allowed = user.Role is LmsRoles.Admin or LmsRoles.Coordinator || cohort.InstructorId == user.Id;
        return allowed ? cohort : AssessmentErrors.Forbidden;
    }

    public async Task<Result<CohortInfo>> TraineeAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var cohort = await cohorts.FindAsync(cohortId, cancellationToken);
        if (cohort is null)
        {
            return AssessmentErrors.CohortNotFound;
        }

        if (user.Id is not { } id || !await cohorts.IsEnrolledAsync(cohortId, id, cancellationToken))
        {
            return AssessmentErrors.NotEnrolled;
        }

        return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) < cohort.StartsOn ? AssessmentErrors.NotStarted : cohort;
    }
}

/// <summary>Tells Learning about every final grade that now exists.</summary>
public sealed class FinalGradePublisher(ICohortDirectory cohorts, ICourseCompletion completion)
{
    public async Task PublishAsync(CohortInfo cohort, Gradebook gradebook, CancellationToken cancellationToken)
    {
        foreach (var traineeId in await cohorts.GetEnrolledTraineesAsync(cohort.Id, cancellationToken))
        {
            if (gradebook.FinalGrade(traineeId) is { } percent)
            {
                await completion.RecordFinalGradeAsync(cohort.Id, cohort.CourseId, traineeId, percent, cancellationToken);
            }
        }
    }
}

/// <summary>Finds a cohort's gradebook, creating it the first time something graded appears.</summary>
public sealed class GradebookAccess(IGradebookRepository gradebooks)
{
    public async Task<Gradebook> OpenAsync(Guid cohortId, CancellationToken cancellationToken)
    {
        var gradebook = await gradebooks.FindAsync(cohortId, cancellationToken);

        if (gradebook is null)
        {
            gradebook = Gradebook.Create(cohortId);
            gradebooks.Add(gradebook);
        }

        return gradebook;
    }
}
