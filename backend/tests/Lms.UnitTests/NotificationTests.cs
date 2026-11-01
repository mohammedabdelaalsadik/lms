using Lms.Modules.Notifications.Application;
using Lms.Modules.Notifications.Contracts;
using Lms.Modules.Notifications.Domain;

namespace Lms.UnitTests;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static Notification New() => Notification.Create(
        Guid.NewGuid(), "Welcome", "key", new NotificationText("عنوان", "Title", "نص", "Body"), "/dashboard", Now);

    [Fact]
    public void Starts_unread_with_its_email_due_now()
    {
        var notification = New();

        Assert.Null(notification.ReadAt);
        Assert.Equal((EmailStatus.Pending, Now), (notification.EmailStatus, notification.NextEmailAttemptAt));
    }

    [Fact]
    public void First_read_time_is_kept()
    {
        var notification = New();

        notification.MarkRead(Now);
        notification.MarkRead(Now.AddHours(1));

        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public void Failures_wait_longer_each_time_then_stop()
    {
        var notification = New();

        notification.RecordEmailFailure(Now);
        Assert.Equal(Now.AddMinutes(1), notification.NextEmailAttemptAt);

        notification.RecordEmailFailure(Now);
        Assert.Equal(Now.AddMinutes(5), notification.NextEmailAttemptAt);

        notification.RecordEmailFailure(Now);
        notification.RecordEmailFailure(Now);
        Assert.Equal(EmailStatus.Pending, notification.EmailStatus);

        notification.RecordEmailFailure(Now);
        Assert.Equal(EmailStatus.Failed, notification.EmailStatus);
    }

    [Fact]
    public void Success_records_the_send()
    {
        var notification = New();

        notification.RecordEmailSent(Now);

        Assert.Equal((EmailStatus.Sent, Now), (notification.EmailStatus, notification.EmailSentAt));
    }

    [Theory]
    [InlineData(NotificationKind.Welcome)]
    [InlineData(NotificationKind.EnrollmentConfirmed)]
    [InlineData(NotificationKind.EnrollmentRejected)]
    [InlineData(NotificationKind.GradesPublished)]
    [InlineData(NotificationKind.GradeCorrected)]
    [InlineData(NotificationKind.CertificateIssued)]
    [InlineData(NotificationKind.CertificateRevoked)]
    [InlineData(NotificationKind.NewMessage)]
    [InlineData(NotificationKind.ForumReply)]
    public void Every_kind_has_complete_text_in_both_languages(NotificationKind kind)
    {
        var text = NotificationTemplates.Render(kind, "دورة", "Course", "ملاحظة", "Note");

        Assert.All(new[] { text.TitleAr, text.TitleEn, text.BodyAr, text.BodyEn }, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.DoesNotContain("{", value);
        });
    }

    [Fact]
    public void Templates_insert_the_course_and_note()
    {
        var text = NotificationTemplates.Render(NotificationKind.EnrollmentRejected, "الدورة", "Excel", "اكتملت المقاعد", "Full");

        Assert.Contains("Excel", text.TitleEn);
        Assert.Contains("اكتملت المقاعد", text.BodyAr);
    }

    [Fact]
    public void Very_long_notes_are_cut_to_fit_storage()
    {
        var text = NotificationTemplates.Render(NotificationKind.EnrollmentRejected, "د", "C", new string('x', 5000), new string('y', 5000));

        Assert.True(text.BodyAr.Length <= Notification.BodyMaxLength);
    }

    [Fact]
    public void Email_escapes_anything_a_person_typed()
    {
        var mail = EmailComposer.Compose("a@b.co", false, "<b>Title</b>", "<script>alert(1)</script>", "https://x.test/a?b=1&c=2", "App", "Open");

        Assert.DoesNotContain("<script>", mail.Html);
        Assert.DoesNotContain("<b>Title", mail.Html);
        Assert.Contains("&lt;script&gt;", mail.Html);
    }

    [Theory]
    [InlineData(true, "rtl", "ar")]
    [InlineData(false, "ltr", "en")]
    public void Email_follows_the_person_language(bool arabic, string direction, string language)
    {
        var mail = EmailComposer.Compose("a@b.co", arabic, "T", "B", "https://x.test", "App", "Open");

        Assert.Contains($"dir=\"{direction}\"", mail.Html);
        Assert.Contains($"lang=\"{language}\"", mail.Html);
    }
}
