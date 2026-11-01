namespace Lms.Modules.Identity.Contracts;

/// <summary>Display data only. The National ID is always masked here.</summary>
public sealed record UserInfo(Guid Id, string NameAr, string NameEn, string MaskedNationalId, string Role, string Email, string Language);

/// <summary>What other modules may ask the Identity module.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserInfo>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Counts by role, and when accounts were created from a date on. Erased accounts are left out.</summary>
    Task<(IReadOnlyDictionary<string, int> ByRole, IReadOnlyList<DateTimeOffset> Joined)> GetStatsAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserInfo>> SearchAsync(string? term, int take, CancellationToken cancellationToken);
}
