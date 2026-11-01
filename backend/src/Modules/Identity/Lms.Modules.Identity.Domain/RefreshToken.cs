using Lms.SharedKernel;

namespace Lms.Modules.Identity.Domain;

public readonly record struct RefreshTokenId(Guid Value);

/// <summary>
/// One use of a long-lived session credential. Each use replaces it with a new token in the same family.
/// Only a hash is stored, so a database leak does not expose working tokens.
/// </summary>
public sealed class RefreshToken : AggregateRoot<RefreshTokenId>
{
    /// <summary>Two browser tabs can refresh at once. A use this soon after rotation is not treated as theft.</summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    private RefreshToken()
    {
    }

    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(UserId userId, string tokenHash, Guid familyId, DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        Id = new RefreshTokenId(Guid.CreateVersion7()),
        UserId = userId,
        TokenHash = tokenHash,
        FamilyId = familyId,
        CreatedAt = now,
        ExpiresAt = expiresAt,
    };

    public bool IsActive(DateTimeOffset now) => RotatedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>True when a token that was already replaced is presented again after the grace period.</summary>
    public bool IsReuse(DateTimeOffset now) => RotatedAt is { } rotated && now - rotated > ReuseGrace;

    public void Rotate(DateTimeOffset now) => RotatedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
