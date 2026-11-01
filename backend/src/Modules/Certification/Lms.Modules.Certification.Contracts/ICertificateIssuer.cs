namespace Lms.Modules.Certification.Contracts;

public interface ICertificateStats
{
    Task<IReadOnlyDictionary<Guid, int>> CountIssuedAsync(IReadOnlyCollection<Guid> cohortIds, CancellationToken cancellationToken);
}

/// <summary>What other modules may ask the Certification module.</summary>
public interface ICertificateIssuer
{
    /// <summary>Safe to call more than once: a completion yields one certificate.</summary>
    Task IssueForCompletionAsync(Guid cohortId, Guid courseId, Guid traineeId, CancellationToken cancellationToken);
}
