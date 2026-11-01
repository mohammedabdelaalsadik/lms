using Lms.Modules.Catalog.Domain;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class NationalIdTests
{
    [Theory]
    [InlineData("1000000008")]
    [InlineData("2000000006")]
    public void Accepts_valid_ids(string value) => Assert.True(NationalId.Create(value).IsSuccess);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("3000000004")]
    [InlineData("1000000009")]
    [InlineData("10000000a8")]
    [InlineData("١٠٠٠٠٠٠٠٠٨")]
    public void Rejects_invalid_ids(string? value)
    {
        var result = NationalId.Create(value);

        Assert.False(result.IsSuccess);
        Assert.Equal(UserErrors.InvalidNationalId, result.Error);
    }

    [Fact]
    public void Never_prints_the_value() =>
        Assert.DoesNotContain("1000000008", NationalId.Create("1000000008").Value.ToString());
}

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Register(
        new ProtectedNationalId("cipher", "hash", "0008"),
        new LocalizedText("متدرب", "Trainee"),
        " Person@Example.COM ",
        "password-hash",
        Role.Trainee,
        Language.Ar,
        Now);

    [Fact]
    public void Masks_all_but_the_last_four_digits() => Assert.Equal("******0008", NewUser().NationalId.Masked);

    [Fact]
    public void Normalizes_email() => Assert.Equal("person@example.com", NewUser().Email);

    [Fact]
    public void Raises_event_without_personal_data()
    {
        var user = NewUser();

        var registered = Assert.IsType<UserRegistered>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id.Value, registered.UserId);
    }

    [Fact]
    public void Locks_after_five_failures_and_unlocks_when_time_passes()
    {
        var user = NewUser();

        for (var i = 0; i < User.MaxFailedLogins - 1; i++)
        {
            user.RecordFailedLogin(Now);
        }

        Assert.False(user.IsLockedOut(Now));

        user.RecordFailedLogin(Now);

        Assert.True(user.IsLockedOut(Now));
        Assert.False(user.IsLockedOut(Now + User.LockoutDuration));
    }

    [Fact]
    public void Successful_login_clears_failures()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin();

        Assert.Equal(0, user.FailedLoginCount);
    }
}

public class CourseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Text = new("نص", "Text");

    private static Course NewCourse() => Course.Create(" saf-101 ", Text, Text, 10, Now).Value;

    [Fact]
    public void Starts_as_draft_with_normalized_code()
    {
        var course = NewCourse();

        Assert.Equal(CourseStatus.Draft, course.Status);
        Assert.Equal("SAF-101", course.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_non_positive_duration(int hours) =>
        Assert.Equal(CourseErrors.InvalidDuration, Course.Create("X", Text, Text, hours, Now).Error);

    [Fact]
    public void Publishing_raises_event_and_blocks_edits()
    {
        var course = NewCourse();

        Assert.True(course.Publish(Now).IsSuccess);

        Assert.IsType<CoursePublished>(Assert.Single(course.DomainEvents));
        Assert.Equal(CourseErrors.OnlyDraftCanChange, course.UpdateDetails(Text, Text, 5).Error);
        Assert.Equal(CourseErrors.OnlyDraftCanPublish, course.Publish(Now).Error);
    }

    [Fact]
    public void Only_published_course_can_be_archived()
    {
        var course = NewCourse();

        Assert.Equal(CourseErrors.OnlyPublishedCanArchive, course.Archive().Error);

        course.Publish(Now);

        Assert.True(course.Archive().IsSuccess);
        Assert.Equal(CourseStatus.Archived, course.Status);
    }
}

public class LocalizedTextTests
{
    [Theory]
    [InlineData("", "en")]
    [InlineData("ar", " ")]
    [InlineData(null, "en")]
    public void Requires_both_languages(string? ar, string? en) =>
        Assert.Equal(CommonErrors.LocalizedTextRequired, LocalizedText.Create(ar, en, 10).Error);

    [Fact]
    public void Rejects_text_over_the_limit() =>
        Assert.Equal(CommonErrors.LocalizedTextTooLong, LocalizedText.Create("ab", "abc", 2).Error);
}
