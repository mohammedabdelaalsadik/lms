using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class PrivacyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static PrivacyRequest Request(PrivacyRequestKind kind, Guid user) => PrivacyRequest.Create(user, kind, Role.Trainee, "سبب", Now).Value;

    private static User Person() => User.Register(
        new ProtectedNationalId("cipher", "hash", "1234"),
        LocalizedText.Create("سارة", "Sara", 150).Value,
        "sara@example.com",
        "hash",
        Role.Trainee,
        Language.Ar,
        Now);

    [Fact]
    public void Staff_cannot_ask_for_erasure() =>
        Assert.Equal(PrivacyErrors.StaffErasure, PrivacyRequest.Create(Guid.NewGuid(), PrivacyRequestKind.Erasure, Role.Instructor, null, Now).Error);

    [Fact]
    public void Staff_can_ask_for_a_copy() =>
        Assert.True(PrivacyRequest.Create(Guid.NewGuid(), PrivacyRequestKind.Export, Role.Admin, null, Now).IsSuccess);

    [Fact]
    public void Nobody_decides_their_own_request()
    {
        var user = Guid.NewGuid();

        Assert.Equal(PrivacyErrors.OwnRequest, Request(PrivacyRequestKind.Export, user).Approve(user, Now).Error);
    }

    [Fact]
    public void A_download_lasts_seven_days_from_approval()
    {
        var request = Request(PrivacyRequestKind.Export, Guid.NewGuid());
        Assert.False(request.CanDownload(Now));

        request.Approve(Guid.NewGuid(), Now);

        Assert.True(request.CanDownload(Now.AddDays(6)));
        Assert.False(request.CanDownload(Now.AddDays(8)));
    }

    [Fact]
    public void An_erasure_request_never_gives_a_download()
    {
        var request = Request(PrivacyRequestKind.Erasure, Guid.NewGuid());

        request.Approve(Guid.NewGuid(), Now);

        Assert.False(request.CanDownload(Now));
    }

    [Fact]
    public void Approving_erasure_drops_the_reason_and_tells_nobody()
    {
        var request = Request(PrivacyRequestKind.Erasure, Guid.NewGuid());
        request.ClearDomainEvents();

        request.Approve(Guid.NewGuid(), Now);

        Assert.Null(request.Reason);
        Assert.Empty(request.DomainEvents);
    }

    [Fact]
    public void Refusing_needs_a_reason() =>
        Assert.Equal(PrivacyErrors.NoteRequired, Request(PrivacyRequestKind.Export, Guid.NewGuid()).Reject(Guid.NewGuid(), " ", Now).Error);

    [Fact]
    public void A_decided_request_cannot_be_decided_again()
    {
        var request = Request(PrivacyRequestKind.Export, Guid.NewGuid());
        request.Approve(Guid.NewGuid(), Now);

        Assert.Equal(PrivacyErrors.NotPending, request.Approve(Guid.NewGuid(), Now).Error);
        Assert.Equal(PrivacyErrors.NotPending, request.Cancel().Error);
    }

    [Fact]
    public void Erasing_leaves_nothing_personal_on_the_account()
    {
        var user = Person();

        user.Erase(Now);

        Assert.True(user.IsErased);
        Assert.Equal(User.ErasedNameEn, user.FullName.En);
        Assert.DoesNotContain("sara", user.Email);
        Assert.Equal("", user.NationalId.Cipher);
        Assert.NotEqual("hash", user.NationalId.LookupHash);
        Assert.Equal("0000", user.NationalId.LastFour);
        Assert.NotEqual("hash", user.PasswordHash);
    }

    [Fact]
    public void Two_erased_accounts_do_not_collide_on_unique_columns()
    {
        var (one, two) = (Person(), Person());

        one.Erase(Now);
        two.Erase(Now);

        Assert.NotEqual(one.Email, two.Email);
        Assert.NotEqual(one.NationalId.LookupHash, two.NationalId.LookupHash);
    }
}
