using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.Security.Cryptography;
using System.Text;

namespace Lms.UnitTests;

public class LiveClassTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static LiveClass Sample(int minutesAway = 60) =>
        LiveClass.Schedule(Guid.NewGuid(), LocalizedText.Create("درس", "Class", 100).Value, Now.AddMinutes(minutesAway), 60, Now).Value;

    [Theory]
    [InlineData(14)]
    [InlineData(481)]
    public void Length_has_limits(int minutes) =>
        Assert.Equal(LiveClassErrors.InvalidDuration, LiveClass.Schedule(Guid.NewGuid(), LocalizedText.Create("د", "C", 100).Value, Now.AddHours(1), minutes, Now).Error);

    [Fact]
    public void A_class_cannot_be_set_in_the_past() =>
        Assert.Equal(LiveClassErrors.InThePast, LiveClass.Schedule(Guid.NewGuid(), LocalizedText.Create("د", "C", 100).Value, Now, 60, Now).Error);

    [Fact]
    public void Staff_can_start_only_near_the_time()
    {
        var live = Sample();

        Assert.Equal(LiveClassErrors.TooEarly, live.CheckCanStart(Now).Error);
        Assert.True(live.CheckCanStart(Now.AddMinutes(45)).IsSuccess);
    }

    [Fact]
    public void Trainees_wait_for_the_class_to_be_opened()
    {
        var live = Sample();

        Assert.Equal(LiveClassErrors.NotStarted, live.CheckCanJoin(Now.AddMinutes(61)).Error);

        live.MarkStarted(Now.AddMinutes(60));

        Assert.True(live.CheckCanJoin(Now.AddMinutes(61)).IsSuccess);
    }

    [Fact]
    public void Ending_closes_the_class()
    {
        var live = Sample();
        live.MarkStarted(Now.AddMinutes(60));

        live.End(Now.AddMinutes(70));

        Assert.Equal(LiveClassErrors.Ended, live.CheckCanJoin(Now.AddMinutes(71)).Error);
        Assert.Equal(LiveClassErrors.Ended, live.CheckCanStart(Now.AddMinutes(71)).Error);
    }

    [Fact]
    public void A_class_past_its_time_counts_as_over()
    {
        var live = Sample();
        live.MarkStarted(Now.AddMinutes(60));

        Assert.Equal(LiveClassStatus.Live, live.StatusAt(Now.AddMinutes(140)));
        Assert.Equal(LiveClassStatus.Ended, live.StatusAt(Now.AddMinutes(160)));
    }

    [Fact]
    public void A_started_class_cannot_be_cancelled()
    {
        var live = Sample();
        Assert.True(live.CheckCanCancel().IsSuccess);

        live.MarkStarted(Now);

        Assert.Equal(LiveClassErrors.AlreadyStarted, live.CheckCanCancel().Error);
    }

    [Fact]
    public void Scheduling_tells_the_cohort()
    {
        var live = Sample();

        Assert.IsType<LiveClassScheduled>(Assert.Single(live.DomainEvents));
    }
}

public class BigBlueButtonUrlTests
{
    private static string Hash(HashAlgorithmName name, string text) =>
        Convert.ToHexStringLower(CryptographicOperations.HashData(name, Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_call_is_signed_from_its_name_query_and_secret()
    {
        var url = BigBlueButtonUrl.Build("https://bbb.example/bigbluebutton/api/", "s3cret", "sha256", "end", [("meetingID", "abc")]);

        Assert.Equal($"https://bbb.example/bigbluebutton/api/end?meetingID=abc&checksum={Hash(HashAlgorithmName.SHA256, "endmeetingID=abcs3cret")}", url);
    }

    [Fact]
    public void Older_servers_can_use_sha1()
    {
        var url = BigBlueButtonUrl.Build("https://bbb.example/api", "k", "sha1", "end", [("meetingID", "abc")]);

        Assert.EndsWith($"checksum={Hash(HashAlgorithmName.SHA1, "endmeetingID=abck")}", url);
    }

    [Fact]
    public void Arabic_names_and_spaces_are_encoded()
    {
        var url = BigBlueButtonUrl.Build("https://bbb.example/api", "k", "sha256", "join", [("fullName", "سارة علي")]);

        Assert.DoesNotContain("سارة", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void The_secret_is_never_in_the_address() =>
        Assert.DoesNotContain("s3cret", BigBlueButtonUrl.Build("https://bbb.example/api", "s3cret", "sha256", "end", [("meetingID", "abc")]));
}
