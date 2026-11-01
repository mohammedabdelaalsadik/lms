using Lms.Modules.Assessment.Domain;
using Lms.Modules.Identity.Application;
using Lms.Modules.Learning.Domain;

namespace Lms.UnitTests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public class SafeExamTests
{
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Url = "https://lms.example/api/v1/quiz";

    [Fact]
    public void The_right_hash_passes() => Assert.True(SafeExam.Matches(Url, Key, SafeExam.Hash(Url, Key)));

    [Fact]
    public void Capitals_in_the_header_do_not_matter() => Assert.True(SafeExam.Matches(Url, Key, SafeExam.Hash(Url, Key).ToUpperInvariant()));

    [Fact]
    public void No_header_is_refused() => Assert.False(SafeExam.Matches(Url, Key, null));

    [Fact]
    public void A_hash_for_another_address_is_refused() => Assert.False(SafeExam.Matches(Url, Key, SafeExam.Hash(Url + "/other", Key)));

    [Fact]
    public void A_hash_from_another_key_is_refused() => Assert.False(SafeExam.Matches(Url, Key, SafeExam.Hash(Url, new string('a', 64))));

    [Fact]
    public void The_hash_is_sha256_of_address_then_key() =>
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SafeExam.Hash("ab", "c"));

    [Theory]
    [InlineData("short")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Bad_keys_are_refused(string key) => Assert.False(SafeExam.IsValidKey(SafeExam.Normalize(key)));

    [Fact]
    public void Keys_are_trimmed_and_lowered() => Assert.Equal(Key, SafeExam.Normalize($" {Key.ToUpperInvariant()} "));
}

public class PrivateFileTests
{
    [Fact]
    public void Files_fit_up_to_the_limit() => Assert.True(PrivateFile.Fits(PrivateFile.QuotaBytes - 10, 10));

    [Fact]
    public void One_byte_over_is_refused() => Assert.False(PrivateFile.Fits(PrivateFile.QuotaBytes - 10, 11));
}

public class PresenceTests
{
    [Fact]
    public void People_drop_off_after_the_window()
    {
        var clock = new FakeTimeProvider();
        var presence = new PresenceTracker(clock);
        var (gone, here) = (Guid.NewGuid(), Guid.NewGuid());

        presence.Touch(gone);
        clock.Advance(PresenceTracker.Window - TimeSpan.FromSeconds(1));
        presence.Touch(here);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(here, Assert.Single(presence.Online()).UserId);
    }

    [Fact]
    public void Being_active_again_keeps_someone_online()
    {
        var clock = new FakeTimeProvider();
        var presence = new PresenceTracker(clock);
        var id = Guid.NewGuid();

        presence.Touch(id);
        clock.Advance(PresenceTracker.Window);
        presence.Touch(id);
        clock.Advance(PresenceTracker.Window);

        Assert.Single(presence.Online());
    }
}
