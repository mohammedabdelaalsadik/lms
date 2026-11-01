using Lms.SharedKernel;
using System.Security.Cryptography;

namespace Lms.Modules.Certification.Domain;

public readonly record struct CertificateId(Guid Value);

public enum CertificateStatus { Issued, Revoked }

public sealed record CertificateIssued(Guid CertificateId, Guid CohortId, Guid CourseId, Guid TraineeId) : IDomainEvent;

public sealed record CertificateRevoked(Guid CertificateId, Guid CourseId, Guid TraineeId, Guid RevokedBy, string Reason) : IDomainEvent;

public static class CertificateErrors
{
    public static readonly Error NotFound = Error.NotFound("certification.not_found", "Certificate was not found.");
    public static readonly Error Forbidden = Error.Forbidden("certification.forbidden", "You cannot open this certificate.");
    public static readonly Error ReasonRequired = Error.Validation("certification.reason_required", "A reason is required.");
    public static readonly Error AlreadyRevoked = Error.Conflict("certification.already_revoked", "This certificate is already revoked.");
    public static readonly Error OnlyRevokedCanReissue = Error.Conflict("certification.only_revoked_can_reissue", "Revoke a certificate before reissuing it.");
    public static readonly Error AlreadyReissued = Error.Conflict("certification.already_reissued", "This certificate has already been reissued.");
    public static readonly Error Revoked = Error.Conflict("certification.revoked", "This certificate has been revoked.");
    public static readonly Error SourceMissing = Error.Conflict("certification.source_missing", "The trainee or course no longer exists.");
}

/// <summary>Names as they stood at issue, so later profile or course edits never alter a certificate.</summary>
public sealed record CertificateDetails(
    string TraineeNameAr,
    string TraineeNameEn,
    string CourseCode,
    string CourseTitleAr,
    string CourseTitleEn,
    DateOnly CompletedOn);

public static class CertificateCode
{
    /// <summary>Crockford base32: no I, L, O or U, so a code read aloud or retyped stays unambiguous.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 12;

    public static string New()
    {
        Span<char> chars = stackalloc char[Length];

        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{chars[..4]}-{chars[4..8]}-{chars[8..]}";
    }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}

/// <summary>Proof of completion. Details never change: a wrong certificate is revoked and reissued.</summary>
public sealed class Certificate : AggregateRoot<CertificateId>
{
    public const int ReasonMaxLength = 500;

    private Certificate()
    {
    }

    public string Code { get; private set; } = null!;
    public Guid CohortId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid TraineeId { get; private set; }
    public string TraineeNameAr { get; private set; } = null!;
    public string TraineeNameEn { get; private set; } = null!;
    public string CourseCode { get; private set; } = null!;
    public string CourseTitleAr { get; private set; } = null!;
    public string CourseTitleEn { get; private set; } = null!;
    public DateOnly CompletedOn { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public CertificateStatus Status { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public string? RevocationReason { get; private set; }
    public Guid? ReplacesId { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static Certificate Issue(Guid cohortId, Guid courseId, Guid traineeId, CertificateDetails details, DateTimeOffset now) =>
        Create(cohortId, courseId, traineeId, details, null, now);

    public Result Revoke(string? reason, Guid revokedBy, DateTimeOffset now)
    {
        if (Status == CertificateStatus.Revoked)
        {
            return CertificateErrors.AlreadyRevoked;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return CertificateErrors.ReasonRequired;
        }

        Status = CertificateStatus.Revoked;
        RevokedAt = now;
        RevokedBy = revokedBy;
        RevocationReason = reason.Trim();
        Raise(new CertificateRevoked(Id.Value, CourseId, TraineeId, revokedBy, RevocationReason));
        return Result.Success();
    }

    /// <summary>Creates the replacement with a new code. The revoked certificate stays on record.</summary>
    public Result<Certificate> Reissue(CertificateDetails details, DateTimeOffset now)
    {
        if (Status != CertificateStatus.Revoked)
        {
            return CertificateErrors.OnlyRevokedCanReissue;
        }

        if (ReplacedById is not null)
        {
            return CertificateErrors.AlreadyReissued;
        }

        var replacement = Create(CohortId, CourseId, TraineeId, details, Id.Value, now);
        ReplacedById = replacement.Id.Value;
        return replacement;
    }

    private static Certificate Create(Guid cohortId, Guid courseId, Guid traineeId, CertificateDetails details, Guid? replaces, DateTimeOffset now)
    {
        var certificate = new Certificate
        {
            Id = new CertificateId(Guid.CreateVersion7()),
            Code = CertificateCode.New(),
            CohortId = cohortId,
            CourseId = courseId,
            TraineeId = traineeId,
            TraineeNameAr = details.TraineeNameAr,
            TraineeNameEn = details.TraineeNameEn,
            CourseCode = details.CourseCode,
            CourseTitleAr = details.CourseTitleAr,
            CourseTitleEn = details.CourseTitleEn,
            CompletedOn = details.CompletedOn,
            IssuedAt = now,
            Status = CertificateStatus.Issued,
            ReplacesId = replaces,
        };

        certificate.Raise(new CertificateIssued(certificate.Id.Value, cohortId, courseId, traineeId));
        return certificate;
    }
}
