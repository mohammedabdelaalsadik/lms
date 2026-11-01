namespace Lms.Modules.Notifications.Contracts;

public enum NotificationKind
{
    Welcome,
    EnrollmentConfirmed,
    EnrollmentRejected,
    GradesPublished,
    GradeCorrected,
    CertificateIssued,
    CertificateRevoked,
    NewMessage,
    ForumReply,
    LearningPlanAssigned,
    PrivacyRequestReceived,
    PrivacyRequestDecided,
    LiveClassScheduled,
}

/// <param name="DedupeKey">Unique per real-world event, so a retried event never notifies twice.</param>
/// <param name="NoteAr">Extra detail such as a reason or item title, in Arabic.</param>
/// <param name="NoteEn">The same detail in English.</param>
/// <param name="Link">The app page that the notification opens.</param>
public sealed record NotificationRequest(
    Guid RecipientId,
    NotificationKind Kind,
    string DedupeKey,
    Guid? CourseId,
    string? NoteAr,
    string? NoteEn,
    string Link);

/// <summary>For secrets that must never be stored, such as a password reset link.</summary>
public interface IDirectMailer
{
    void QueuePasswordReset(string email, bool arabic, string link);
}

/// <summary>What other modules may ask the Notifications module.</summary>
public interface INotifier
{
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken);
}
