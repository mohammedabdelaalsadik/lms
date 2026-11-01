namespace Lms.Modules.Learning.Contracts;

public sealed record CohortProgressStat(int Completed, decimal? AverageGrade);

public interface ICohortProgressStats
{
    Task<IReadOnlyDictionary<Guid, CohortProgressStat>> GetAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken);
}

public sealed record UpcomingLiveClass(string TitleAr, string TitleEn, DateTimeOffset StartsAt);

public interface ILiveClassSchedule
{
    /// <summary>Classes in a cohort that have not ended yet.</summary>
    Task<IReadOnlyList<UpcomingLiveClass>> ListOpenAsync(Guid cohortId, CancellationToken cancellationToken);
}

/// <summary>What other modules may tell the Learning module.</summary>
public interface ICourseCompletion
{
    /// <summary>Hands over a trainee's final grade. Learning decides what it means for completion.</summary>
    Task RecordFinalGradeAsync(Guid cohortId, Guid courseId, Guid traineeId, decimal percent, CancellationToken cancellationToken);
}
