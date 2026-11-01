using Lms.Modules.Certification.Domain;

namespace Lms.UnitTests;

public class CertificateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private static readonly CertificateDetails Details = new(
        "سارة", "Sara", "DAT-120", "تحليل البيانات", "Data Analysis", new DateOnly(2026, 11, 1));

    private static Certificate New() => Certificate.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, Now);

    [Fact]
    public void Issue_raises_an_event_with_no_personal_details()
    {
        var certificate = New();

        var issued = Assert.IsType<CertificateIssued>(Assert.Single(certificate.DomainEvents));
        Assert.Equal(certificate.Id.Value, issued.CertificateId);
        Assert.Equal(CertificateStatus.Issued, certificate.Status);
    }

    [Fact]
    public void Codes_are_formatted_unambiguous_and_unique()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => CertificateCode.New()).ToList();

        Assert.All(codes, code => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData(" ab12-cd34-ef56 ", "AB12-CD34-EF56")]
    [InlineData("AB12-CD34-EF56", "AB12-CD34-EF56")]
    public void Lookup_ignores_case_and_spaces(string typed, string expected) =>
        Assert.Equal(expected, CertificateCode.Normalize(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Revocation_needs_a_reason(string? reason)
    {
        var certificate = New();

        Assert.Equal(CertificateErrors.ReasonRequired, certificate.Revoke(reason, Admin, Now).Error);
        Assert.Equal(CertificateStatus.Issued, certificate.Status);
    }

    [Fact]
    public void Revoking_records_who_why_and_when_and_happens_once()
    {
        var certificate = New();

        Assert.True(certificate.Revoke(" خطأ في الاسم ", Admin, Now).IsSuccess);

        Assert.Equal(("خطأ في الاسم", Admin, Now), (certificate.RevocationReason, certificate.RevokedBy, certificate.RevokedAt));
        Assert.Equal(CertificateErrors.AlreadyRevoked, certificate.Revoke("x", Admin, Now).Error);
    }

    [Fact]
    public void Only_a_revoked_certificate_can_be_reissued()
    {
        Assert.Equal(CertificateErrors.OnlyRevokedCanReissue, New().Reissue(Details, Now).Error);
    }

    [Fact]
    public void Reissue_gets_a_new_code_and_links_both_ways()
    {
        var original = New();
        original.Revoke("x", Admin, Now);

        var replacement = original.Reissue(Details with { TraineeNameEn = "Sarah" }, Now).Value;

        Assert.NotEqual(original.Code, replacement.Code);
        Assert.Equal(replacement.Id.Value, original.ReplacedById);
        Assert.Equal(original.Id.Value, replacement.ReplacesId);
        Assert.Equal("Sarah", replacement.TraineeNameEn);
        Assert.Equal("Sara", original.TraineeNameEn);
        Assert.Equal(CertificateStatus.Revoked, original.Status);
    }

    [Fact]
    public void A_certificate_is_reissued_only_once()
    {
        var original = New();
        original.Revoke("x", Admin, Now);
        original.Reissue(Details, Now);

        Assert.Equal(CertificateErrors.AlreadyReissued, original.Reissue(Details, Now).Error);
    }
}
