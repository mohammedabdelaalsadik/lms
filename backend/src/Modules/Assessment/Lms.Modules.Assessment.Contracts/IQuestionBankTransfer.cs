namespace Lms.Modules.Assessment.Contracts;

public sealed record BankOption(string TextAr, string TextEn, bool IsCorrect, string? MatchAr, string? MatchEn);

public sealed record BankEntry(string Category, string Kind, string TextAr, string TextEn, int Points, decimal? Tolerance, IReadOnlyList<BankOption> Options);

/// <summary>Moves a course's question bank in and out of a course backup. Accepted answers are included, so callers must stay staff-only.</summary>
public interface IQuestionBankTransfer
{
    Task<IReadOnlyList<BankEntry>> ExportAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>Returns how many were added. Entries that break a bank rule are skipped.</summary>
    Task<int> ImportAsync(Guid courseId, IReadOnlyList<BankEntry> entries, Guid createdBy, CancellationToken cancellationToken);
}
