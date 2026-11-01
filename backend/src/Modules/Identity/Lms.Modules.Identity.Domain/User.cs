using Lms.SharedKernel;

namespace Lms.Modules.Identity.Domain;

public readonly record struct UserId(Guid Value);

public enum Role { Trainee, Instructor, Coordinator, Admin, Auditor }

public enum Language { Ar, En }

public sealed record UserRegistered(Guid UserId, Role Role) : IDomainEvent;

public static class UserErrors
{
    public static readonly Error InvalidNationalId = Error.Validation("identity.national_id.invalid", "The National ID is not valid.");
    public static readonly Error NationalIdTaken = Error.Conflict("identity.national_id.taken", "This National ID is already registered.");
    public static readonly Error EmailTaken = Error.Conflict("identity.email.taken", "This email is already registered.");
    public static readonly Error InvalidCredentials = Error.Unauthorized("identity.invalid_credentials", "The National ID or password is incorrect.");
    public static readonly Error NotFound = Error.NotFound("identity.user.not_found", "User was not found.");
    public static readonly Error InvalidRefreshToken = Error.Unauthorized("identity.invalid_refresh_token", "The session has ended.");
    public static readonly Error InvalidMfaCode = Error.Unauthorized("identity.invalid_mfa_code", "The code is not correct.");
    public static readonly Error MfaAlreadyEnabled = Error.Conflict("identity.mfa_already_enabled", "Two-step sign-in is already on.");
    public static readonly Error MfaRequired = Error.Forbidden("identity.mfa_required", "Staff accounts must keep two-step sign-in on.");
    public static readonly Error CannotResetOwnMfa = Error.Conflict("identity.cannot_reset_own_mfa", "Another administrator must do this.");
    public static readonly Error InvalidResetLink = Error.Validation("identity.invalid_reset_link", "This link has expired or was already used.");
    public static readonly Error WrongPassword = Error.Validation("identity.wrong_password", "The current password is not correct.");
    public static readonly Error SamePassword = Error.Validation("identity.same_password", "Choose a different password.");
    public static readonly Error UploadInvalid = Error.Validation("identity.upload_invalid", "The file needs the columns national_id, name_ar, name_en and email.");
    public static readonly Error UploadTooLarge = Error.Validation("identity.upload_too_large", "A file can hold at most 1000 people.");
    public static readonly Error UploadName = Error.Validation("identity.upload_name", "Both names are needed.");
    public static readonly Error UploadEmail = Error.Validation("identity.upload_email", "The email is not valid.");
    public static readonly Error UploadRole = Error.Validation("identity.upload_role", "This role cannot be given by file.");
    public static readonly Error CannotChangeOwnRole = Error.Conflict("identity.cannot_change_own_role", "You cannot change your own role.");
}

/// <summary>A validated Saudi National or Iqama ID. It exists in plain form only while being protected.</summary>
public sealed record NationalId
{
    private NationalId(string value) => Value = value;

    public string Value { get; }

    public static Result<NationalId> Create(string? value)
    {
        value = value?.Trim();

        if (value is not { Length: 10 } || !value.All(char.IsAsciiDigit) || value[0] is not ('1' or '2'))
        {
            return UserErrors.InvalidNationalId;
        }

        return HasValidCheckDigit(value) ? new NationalId(value) : UserErrors.InvalidNationalId;
    }

    public override string ToString() => "**********";

    private static bool HasValidCheckDigit(string value)
    {
        var sum = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var digit = value[i] - '0';

            if (i % 2 == 0)
            {
                digit *= 2;
                digit = digit > 9 ? digit - 9 : digit;
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }
}

/// <summary>
/// How a National ID is stored: encrypted, plus a keyed hash for exact-match lookup
/// and the last four digits for masked display.
/// </summary>
public sealed record ProtectedNationalId(string Cipher, string LookupHash, string LastFour)
{
    public string Masked => $"******{LastFour}";
}

public sealed class User : AggregateRoot<UserId>
{
    public const int MaxFailedLogins = 5;
    public const string ErasedNameAr = "حساب محذوف";
    public const string ErasedNameEn = "Deleted account";
    public const int NameMaxLength = 150;
    public const int EmailMaxLength = 254;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);

    private User()
    {
    }

    public ProtectedNationalId NationalId { get; private set; } = null!;
    public LocalizedText FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public Role Role { get; private set; }
    public Language Language { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? MfaSecretCipher { get; private set; }
    public DateTimeOffset? MfaEnabledAt { get; private set; }
    public long MfaLastStep { get; private set; }
    public string? MfaRecoveryHashes { get; private set; }

    public bool MustChangePassword { get; private set; }
    public string? ResetTokenHash { get; private set; }
    public DateTimeOffset? ResetExpiresAt { get; private set; }

    public bool MfaEnabled => MfaEnabledAt is not null;
    public DateTimeOffset? ErasedAt { get; private set; }
    public bool IsErased => ErasedAt is not null;

    /// <summary>
    /// Keeps the row, because grades and certificates point at it, and removes everything that identifies the person.
    /// The unique columns take a value derived from the id, which can never match a real National ID or email.
    /// </summary>
    public void Erase(DateTimeOffset now)
    {
        var marker = Id.Value.ToString("N");
        NationalId = new ProtectedNationalId("", $"erased:{marker}", "0000");
        FullName = LocalizedText.Create(ErasedNameAr, ErasedNameEn, NameMaxLength).Value;
        Email = $"erased-{marker}@erased.invalid";
        PasswordHash = "erased";
        MfaSecretCipher = null;
        MfaEnabledAt = null;
        MfaRecoveryHashes = null;
        ResetTokenHash = null;
        ResetExpiresAt = null;
        ErasedAt = now;
    }

    public static User Register(
        ProtectedNationalId nationalId,
        LocalizedText fullName,
        string email,
        string passwordHash,
        Role role,
        Language language,
        DateTimeOffset now)
    {
        var user = new User
        {
            Id = new UserId(Guid.CreateVersion7()),
            NationalId = nationalId,
            FullName = fullName,
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role,
            Language = language,
            CreatedAt = now,
        };

        user.Raise(new UserRegistered(user.Id.Value, role));
        return user;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil > now;

    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;

        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockedUntil = now + LockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public bool NeedsMfaSetup(bool requiredForStaff) => requiredForStaff && Role != Role.Trainee && !MfaEnabled;

    public Result BeginMfaSetup(string secretCipher)
    {
        if (MfaEnabled)
        {
            return UserErrors.MfaAlreadyEnabled;
        }

        MfaSecretCipher = secretCipher;
        return Result.Success();
    }

    public void CompleteMfaSetup(long step, IReadOnlyCollection<string> recoveryHashes, DateTimeOffset now)
    {
        MfaEnabledAt = now;
        MfaLastStep = step;
        MfaRecoveryHashes = string.Join(',', recoveryHashes);
    }

    public void RecordMfaStep(long step) => MfaLastStep = step;

    public bool UseRecoveryCode(string hash)
    {
        var remaining = (MfaRecoveryHashes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (!remaining.Remove(hash))
        {
            return false;
        }

        MfaRecoveryHashes = string.Join(',', remaining);
        return true;
    }

    public void ClearMfa()
    {
        MfaSecretCipher = null;
        MfaEnabledAt = null;
        MfaLastStep = 0;
        MfaRecoveryHashes = null;
    }

    public void BeginPasswordReset(string tokenHash, DateTimeOffset now)
    {
        ResetTokenHash = tokenHash;
        ResetExpiresAt = now + ResetLifetime;
    }

    public bool ResetIsValid(DateTimeOffset now) => ResetTokenHash is not null && now < ResetExpiresAt;

    public void CompletePasswordReset(string passwordHash)
    {
        ChangePassword(passwordHash);
        ResetTokenHash = null;
        ResetExpiresAt = null;
        RecordSuccessfulLogin();
    }

    public void RequirePasswordChange() => MustChangePassword = true;

    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        MustChangePassword = false;
    }

    public void ChangeLanguage(Language language) => Language = language;

    public void ChangeRole(Role role) => Role = role;
}
