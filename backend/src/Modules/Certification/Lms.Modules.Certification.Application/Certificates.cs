using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Certification.Contracts;
using Lms.Modules.Certification.Domain;
using Lms.Modules.Identity.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Certification.Application;

public interface ICertificationUnitOfWork : IUnitOfWork;

public sealed record CertificateFilter(CertificateStatus? Status, string? Search, int Page, int PageSize);

public interface ICertificateRepository
{
    Task<Certificate?> FindAsync(CertificateId id, CancellationToken cancellationToken);

    Task<Certificate?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<bool> ExistsForAsync(Guid cohortId, Guid traineeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Certificate>> ListForTraineeAsync(Guid traineeId, CancellationToken cancellationToken);

    Task<PagedResult<Certificate>> ListAsync(CertificateFilter filter, CancellationToken cancellationToken);

    void Add(Certificate certificate);
}

public interface ICertificatePdf
{
    byte[] Render(Certificate certificate);
}

public sealed record TextDto(string Ar, string En);

public sealed record CertificateResponse(
    Guid Id,
    string Code,
    TextDto TraineeName,
    string CourseCode,
    TextDto CourseTitle,
    DateOnly CompletedOn,
    DateTimeOffset IssuedAt,
    CertificateStatus Status,
    DateTimeOffset? RevokedAt,
    string? RevocationReason,
    Guid? ReplacedById);

/// <summary>All the public may see. Carries no National ID, internal ids or revocation reason.</summary>
public sealed record CertificateVerification(
    string Code,
    CertificateStatus Status,
    TextDto TraineeName,
    string CourseCode,
    TextDto CourseTitle,
    DateOnly CompletedOn,
    DateTimeOffset IssuedAt,
    DateTimeOffset? RevokedAt);

internal static class CertificateMapping
{
    public static CertificateResponse ToResponse(this Certificate c) => new(
        c.Id.Value,
        c.Code,
        new TextDto(c.TraineeNameAr, c.TraineeNameEn),
        c.CourseCode,
        new TextDto(c.CourseTitleAr, c.CourseTitleEn),
        c.CompletedOn,
        c.IssuedAt,
        c.Status,
        c.RevokedAt,
        c.RevocationReason,
        c.ReplacedById);
}

/// <summary>Reads names from their owning modules at the moment of issue.</summary>
public sealed class CertificateDetailsReader(ICourseCatalog catalog, IUserDirectory directory, TimeProvider clock)
{
    public async Task<CertificateDetails?> ReadAsync(Guid courseId, Guid traineeId, DateOnly? completedOn, CancellationToken cancellationToken)
    {
        var courses = await catalog.GetAsync([courseId], cancellationToken);
        var users = await directory.GetAsync([traineeId], cancellationToken);

        if (!courses.TryGetValue(courseId, out var course) || !users.TryGetValue(traineeId, out var user))
        {
            return null;
        }

        return new CertificateDetails(
            user.NameAr,
            user.NameEn,
            course.Code,
            course.TitleAr,
            course.TitleEn,
            completedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}

public sealed class CertificateIssuer(
    ICertificateRepository certificates,
    CertificateDetailsReader reader,
    BadgeAwarder badges,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : ICertificateIssuer
{
    public async Task IssueForCompletionAsync(Guid cohortId, Guid courseId, Guid traineeId, CancellationToken cancellationToken)
    {
        if (await certificates.ExistsForAsync(cohortId, traineeId, cancellationToken))
        {
            return;
        }

        var details = await reader.ReadAsync(courseId, traineeId, null, cancellationToken)
            ?? throw new InvalidOperationException($"Cannot read the details for a certificate in cohort {cohortId}.");

        certificates.Add(Certificate.Issue(cohortId, courseId, traineeId, details, clock.GetUtcNow()));
        await badges.AwardAsync(courseId, traineeId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ListMyCertificatesQuery : IQuery<IReadOnlyList<CertificateResponse>>;

internal sealed class ListMyCertificatesHandler(ICertificateRepository certificates, ICurrentUser user)
    : IRequestHandler<ListMyCertificatesQuery, IReadOnlyList<CertificateResponse>>
{
    public async Task<Result<IReadOnlyList<CertificateResponse>>> Handle(ListMyCertificatesQuery request, CancellationToken cancellationToken)
    {
        var list = await certificates.ListForTraineeAsync(user.Id!.Value, cancellationToken);
        IReadOnlyList<CertificateResponse> items = [.. list.Select(c => c.ToResponse())];
        return Result<IReadOnlyList<CertificateResponse>>.Success(items);
    }
}

public sealed record ListCertificatesQuery(CertificateStatus? Status, string? Search, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<CertificateResponse>>;

internal sealed class ListCertificatesValidator : AbstractValidator<ListCertificatesQuery>
{
    public ListCertificatesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListCertificatesHandler(ICertificateRepository certificates)
    : IRequestHandler<ListCertificatesQuery, PagedResult<CertificateResponse>>
{
    public async Task<Result<PagedResult<CertificateResponse>>> Handle(ListCertificatesQuery request, CancellationToken cancellationToken)
    {
        var page = await certificates.ListAsync(new CertificateFilter(request.Status, request.Search, request.Page, request.PageSize), cancellationToken);
        return new PagedResult<CertificateResponse>([.. page.Items.Select(c => c.ToResponse())], page.Total, page.Page, page.PageSize);
    }
}

public sealed record VerifyCertificateQuery(string Code) : IQuery<CertificateVerification>;

internal sealed class VerifyCertificateHandler(ICertificateRepository certificates)
    : IRequestHandler<VerifyCertificateQuery, CertificateVerification>
{
    public async Task<Result<CertificateVerification>> Handle(VerifyCertificateQuery request, CancellationToken cancellationToken)
    {
        var certificate = await certificates.FindByCodeAsync(CertificateCode.Normalize(request.Code), cancellationToken);
        if (certificate is null)
        {
            return CertificateErrors.NotFound;
        }

        return new CertificateVerification(
            certificate.Code,
            certificate.Status,
            new TextDto(certificate.TraineeNameAr, certificate.TraineeNameEn),
            certificate.CourseCode,
            new TextDto(certificate.CourseTitleAr, certificate.CourseTitleEn),
            certificate.CompletedOn,
            certificate.IssuedAt,
            certificate.RevokedAt);
    }
}

public sealed record RevokeCertificateCommand(Guid Id, string Reason) : ICommand<Unit>;

internal sealed class RevokeCertificateValidator : AbstractValidator<RevokeCertificateCommand>
{
    public RevokeCertificateValidator() => RuleFor(c => c.Reason).MaximumLength(Certificate.ReasonMaxLength);
}

internal sealed class RevokeCertificateHandler(
    ICertificateRepository certificates,
    IBadgeRepository badges,
    ICurrentUser user,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RevokeCertificateCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RevokeCertificateCommand request, CancellationToken cancellationToken)
    {
        var certificate = await certificates.FindAsync(new CertificateId(request.Id), cancellationToken);
        if (certificate is null)
        {
            return CertificateErrors.NotFound;
        }

        var result = certificate.Revoke(request.Reason, user.Id!.Value, clock.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await badges.RemoveAwardsAsync(certificate.CourseId, certificate.TraineeId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ReissueCertificateCommand(Guid Id) : ICommand<CertificateResponse>;

internal sealed class ReissueCertificateHandler(
    ICertificateRepository certificates,
    CertificateDetailsReader reader,
    BadgeAwarder badges,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ReissueCertificateCommand, CertificateResponse>
{
    public async Task<Result<CertificateResponse>> Handle(ReissueCertificateCommand request, CancellationToken cancellationToken)
    {
        var certificate = await certificates.FindAsync(new CertificateId(request.Id), cancellationToken);
        if (certificate is null)
        {
            return CertificateErrors.NotFound;
        }

        // Current names are read again, because a wrong name is the usual reason to reissue.
        var details = await reader.ReadAsync(certificate.CourseId, certificate.TraineeId, certificate.CompletedOn, cancellationToken);
        if (details is null)
        {
            return CertificateErrors.SourceMissing;
        }

        var replacement = certificate.Reissue(details, clock.GetUtcNow());
        if (!replacement.IsSuccess)
        {
            return replacement.Error!;
        }

        certificates.Add(replacement.Value);
        await badges.AwardAsync(certificate.CourseId, certificate.TraineeId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return replacement.Value.ToResponse();
    }
}

public sealed record CertificatePdfFile(byte[] Content, string FileName);

public sealed record DownloadCertificateQuery(Guid Id) : IQuery<CertificatePdfFile>, IAuditedRequest;

internal sealed class DownloadCertificateHandler(ICertificateRepository certificates, ICertificatePdf pdf, ICurrentUser user)
    : IRequestHandler<DownloadCertificateQuery, CertificatePdfFile>
{
    public async Task<Result<CertificatePdfFile>> Handle(DownloadCertificateQuery request, CancellationToken cancellationToken)
    {
        var certificate = await certificates.FindAsync(new CertificateId(request.Id), cancellationToken);
        if (certificate is null)
        {
            return CertificateErrors.NotFound;
        }

        var isStaff = user.Role is LmsRoles.Admin or LmsRoles.Coordinator or LmsRoles.Auditor;

        if (!isStaff && certificate.TraineeId != user.Id)
        {
            return CertificateErrors.Forbidden;
        }

        if (!isStaff && certificate.Status == CertificateStatus.Revoked)
        {
            return CertificateErrors.Revoked;
        }

        return new CertificatePdfFile(pdf.Render(certificate), $"certificate-{certificate.Code}.pdf");
    }
}
