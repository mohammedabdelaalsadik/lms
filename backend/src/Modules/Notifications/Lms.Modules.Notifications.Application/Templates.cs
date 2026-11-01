using Lms.Modules.Notifications.Contracts;
using Lms.Modules.Notifications.Domain;

namespace Lms.Modules.Notifications.Application;

/// <summary>All notification wording, in one place and both languages.</summary>
public static class NotificationTemplates
{
    private sealed record Template(string TitleAr, string TitleEn, string BodyAr, string BodyEn);

    private static readonly Dictionary<NotificationKind, Template> Templates = new()
    {
        [NotificationKind.Welcome] = new(
            "مرحبًا بك في منصة التدريب",
            "Welcome to the Training Platform",
            "تم إنشاء حسابك. يمكنك الآن تصفح الدورات والتسجيل في الدفعات المتاحة.",
            "Your account is ready. You can now browse courses and enroll in open cohorts."),
        [NotificationKind.EnrollmentConfirmed] = new(
            "تم تسجيلك في {course}",
            "You're enrolled in {course}",
            "تأكد تسجيلك في الدورة. ستجد المحتوى في صفحة تعلّمي عند بدء الدفعة.",
            "Your place is confirmed. The content appears under My learning when the cohort starts."),
        [NotificationKind.EnrollmentRejected] = new(
            "لم يُقبل طلب تسجيلك في {course}",
            "Your request for {course} wasn't approved",
            "السبب: {note}",
            "Reason: {note}"),
        [NotificationKind.GradesPublished] = new(
            "نُشرت درجة جديدة في {course}",
            "A new grade is available in {course}",
            "تم نشر درجات {note}. يمكنك الاطلاع عليها الآن.",
            "Grades for {note} are now published."),
        [NotificationKind.GradeCorrected] = new(
            "تم تصحيح درجتك في {course}",
            "Your grade in {course} was corrected",
            "تم اعتماد تصحيح إحدى درجاتك. راجع درجاتك للاطلاع عليها.",
            "A correction to one of your grades was approved. Check your grades to see it."),
        [NotificationKind.CertificateIssued] = new(
            "شهادتك في {course} جاهزة",
            "Your {course} certificate is ready",
            "تهانينا على إتمام الدورة. يمكنك تنزيل شهادتك الآن.",
            "Congratulations on completing the course. You can download your certificate now."),
        [NotificationKind.NewMessage] = new(
            "رسالة جديدة من {note}",
            "New message from {note}",
            "افتح المنصة لقراءة الرسالة والرد عليها.",
            "Open the platform to read and reply."),
        [NotificationKind.ForumReply] = new(
            "رد جديد على موضوعك",
            "New reply to your topic",
            "أضاف أحد المشاركين ردًا على: {note}",
            "Someone replied to: {note}"),
        [NotificationKind.LearningPlanAssigned] = new(
            "خطة تعلّم جديدة",
            "A new learning plan",
            "أُسندت إليك خطة: {note}. افتح خططي للاطلاع على المهارات المطلوبة.",
            "You have been given a plan: {note}. Open My plans to see the skills it asks for."),
        [NotificationKind.PrivacyRequestReceived] = new(
            "طلب خصوصية جديد",
            "A new privacy request",
            "ينتظر طلب خصوصية قرارك.",
            "A privacy request is waiting for your decision."),
        [NotificationKind.PrivacyRequestDecided] = new(
            "تم الرد على طلب الخصوصية",
            "Your privacy request was answered",
            "افتح صفحة الخصوصية للاطلاع على النتيجة.",
            "Open the Privacy page to see the result."),
        [NotificationKind.LiveClassScheduled] = new(
            "درس مباشر جديد",
            "A new live class",
            "تمت جدولة درس مباشر: {note}. افتح الدروس المباشرة للاطلاع على موعده.",
            "A live class was scheduled: {note}. Open Live classes to see when."),
        [NotificationKind.CertificateRevoked] = new(
            "تم إلغاء شهادتك في {course}",
            "Your {course} certificate was revoked",
            "السبب: {note}",
            "Reason: {note}"),
    };

    public static NotificationText Render(NotificationKind kind, string? courseAr, string? courseEn, string? noteAr, string? noteEn)
    {
        var template = Templates[kind];

        return new NotificationText(
            Fit(Fill(template.TitleAr, courseAr, noteAr), Notification.TitleMaxLength),
            Fit(Fill(template.TitleEn, courseEn, noteEn), Notification.TitleMaxLength),
            Fit(Fill(template.BodyAr, courseAr, noteAr), Notification.BodyMaxLength),
            Fit(Fill(template.BodyEn, courseEn, noteEn), Notification.BodyMaxLength));
    }

    private static string Fill(string text, string? course, string? note) =>
        text.Replace("{course}", course ?? "", StringComparison.Ordinal).Replace("{note}", note ?? "", StringComparison.Ordinal);

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..max];
}
