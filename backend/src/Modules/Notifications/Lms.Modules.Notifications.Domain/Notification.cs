using Lms.SharedKernel;

namespace Lms.Modules.Notifications.Domain;

public readonly record struct NotificationId(Guid Value);

public enum EmailStatus { Pending, Sent, Failed }

public static class NotificationErrors
{
    public static readonly Error NotFound = Error.NotFound("notifications.not_found", "Notification was not found.");
}

public sealed record NotificationText(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

/// <summary>A message to one person: shown in the app and sent once by email. Text is fixed when created.</summary>
public sealed class Notification : AggregateRoot<NotificationId>
{
    public const int MaxEmailAttempts = 5;
    public const int TitleMaxLength = 300;
    public const int BodyMaxLength = 1200;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    ];

    private Notification()
    {
    }

    public Guid RecipientId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string DedupeKey { get; private set; } = null!;
    public string TitleAr { get; private set; } = null!;
    public string TitleEn { get; private set; } = null!;
    public string BodyAr { get; private set; } = null!;
    public string BodyEn { get; private set; } = null!;
    public string Link { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public EmailStatus EmailStatus { get; private set; }
    public int EmailAttempts { get; private set; }
    public DateTimeOffset NextEmailAttemptAt { get; private set; }
    public DateTimeOffset? EmailSentAt { get; private set; }

    public static Notification Create(Guid recipientId, string kind, string dedupeKey, NotificationText text, string link, DateTimeOffset now) => new()
    {
        Id = new NotificationId(Guid.CreateVersion7()),
        RecipientId = recipientId,
        Kind = kind,
        DedupeKey = dedupeKey,
        TitleAr = text.TitleAr,
        TitleEn = text.TitleEn,
        BodyAr = text.BodyAr,
        BodyEn = text.BodyEn,
        Link = link,
        CreatedAt = now,
        EmailStatus = EmailStatus.Pending,
        NextEmailAttemptAt = now,
    };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void RecordEmailSent(DateTimeOffset now)
    {
        EmailStatus = EmailStatus.Sent;
        EmailSentAt = now;
        EmailAttempts++;
    }

    /// <summary>Waits longer after each failure, then stops. The in-app copy is unaffected.</summary>
    public void RecordEmailFailure(DateTimeOffset now)
    {
        EmailAttempts++;

        if (EmailAttempts >= MaxEmailAttempts)
        {
            EmailStatus = EmailStatus.Failed;
            return;
        }

        NextEmailAttemptAt = now + RetryDelays[EmailAttempts - 1];
    }

    public void AbandonEmail() => EmailStatus = EmailStatus.Failed;
}
