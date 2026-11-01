using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public sealed class UserAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Register(
        new ProtectedNationalId("cipher", "lookup", "0006"),
        LocalizedText.Create("سارة", "Sara", User.NameMaxLength).Value,
        "sara@example.com",
        "hash",
        Role.Trainee,
        Language.Ar,
        Now);

    [Fact]
    public void A_new_account_is_active()
    {
        Assert.True(NewUser().IsActive);
    }

    [Fact]
    public void Deactivating_cancels_any_reset_link()
    {
        var user = NewUser();
        user.BeginPasswordReset("token", Now);

        user.Deactivate(Now);

        Assert.False(user.IsActive);
        Assert.False(user.ResetIsValid(Now));
    }

    [Fact]
    public void Reactivating_restores_the_account()
    {
        var user = NewUser();
        user.Deactivate(Now);

        user.Reactivate();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void An_erased_account_does_not_count_as_active()
    {
        var user = NewUser();

        user.Erase(Now);

        Assert.False(user.IsActive);
    }

    [Fact]
    public void Changing_the_profile_cleans_the_email()
    {
        var user = NewUser();

        user.ChangeProfile(LocalizedText.Create("نورة", "Noura", User.NameMaxLength).Value, "  Noura@Example.COM ");

        Assert.Equal("Noura", user.FullName.En);
        Assert.Equal("noura@example.com", user.Email);
    }
}
