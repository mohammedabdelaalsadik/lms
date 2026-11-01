using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using System.Text;

namespace Lms.UnitTests;

public class TotpTests
{
    // The published test secret and values from RFC 6238, cut to six digits.
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(20000000000, "353130")]
    public void Matches_the_published_test_values(long seconds, string expected) =>
        Assert.Equal(expected, Totp.Compute(Secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(seconds))));

    [Fact]
    public void Base32_matches_the_standard_example() =>
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(Secret));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Accepts_one_step_of_clock_drift(int drift)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now) + drift;

        Assert.Equal(step, Totp.Match(Secret, Totp.Compute(Secret, step), now, 0));
    }

    [Fact]
    public void Refuses_an_old_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        Assert.Null(Totp.Match(Secret, Totp.Compute(Secret, Totp.StepAt(now) - 2), now, 0));
    }

    [Fact]
    public void Refuses_a_code_used_twice()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);

        Assert.Null(Totp.Match(Secret, Totp.Compute(Secret, step), now, step));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12345a")]
    public void Refuses_malformed_codes(string? code) =>
        Assert.Null(Totp.Match(Secret, code, DateTimeOffset.UnixEpoch, 0));
}

public class UserMfaTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static User New(Role role = Role.Admin) => User.Register(
        new ProtectedNationalId("c", "h", "0008"), new LocalizedText("أ", "A"), "a@b.co", "hash", role, Language.Ar, Now);

    private static User Enabled()
    {
        var user = New();
        user.BeginMfaSetup("cipher");
        user.CompleteMfaSetup(10, ["one", "two"], Now);
        return user;
    }

    [Fact]
    public void Starting_setup_does_not_turn_it_on()
    {
        var user = New();

        user.BeginMfaSetup("cipher");

        Assert.False(user.MfaEnabled);
    }

    [Fact]
    public void Setup_cannot_replace_a_working_second_factor() =>
        Assert.Equal(UserErrors.MfaAlreadyEnabled, Enabled().BeginMfaSetup("other").Error);

    [Fact]
    public void Recovery_code_works_once()
    {
        var user = Enabled();

        Assert.True(user.UseRecoveryCode("one"));
        Assert.False(user.UseRecoveryCode("one"));
        Assert.True(user.UseRecoveryCode("two"));
    }

    [Fact]
    public void Unknown_recovery_code_is_refused() => Assert.False(Enabled().UseRecoveryCode("nope"));

    [Theory]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Auditor, true)]
    [InlineData(Role.Instructor, true)]
    [InlineData(Role.Coordinator, true)]
    [InlineData(Role.Trainee, false)]
    public void Only_staff_must_set_it_up(Role role, bool expected) =>
        Assert.Equal(expected, New(role).NeedsMfaSetup(true));

    [Fact]
    public void Nobody_must_set_it_up_while_the_policy_is_off() => Assert.False(New().NeedsMfaSetup(false));

    [Fact]
    public void Staff_with_it_on_have_nothing_to_do() => Assert.False(Enabled().NeedsMfaSetup(true));

    [Fact]
    public void Clearing_removes_the_secret_and_codes()
    {
        var user = Enabled();

        user.ClearMfa();

        Assert.False(user.MfaEnabled);
        Assert.Null(user.MfaSecretCipher);
        Assert.False(user.UseRecoveryCode("one"));
    }
}
