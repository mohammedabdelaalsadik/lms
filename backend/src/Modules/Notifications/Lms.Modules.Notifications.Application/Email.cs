using System.Net;

namespace Lms.Modules.Notifications.Application;

public sealed record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Builds the email in the person's language. Every inserted value is escaped.</summary>
public static class EmailComposer
{
    public static EmailMessage Compose(string to, bool arabic, string title, string body, string url, string appName, string openLabel)
    {
        var direction = arabic ? "rtl" : "ltr";
        var language = arabic ? "ar" : "en";
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeBody = WebUtility.HtmlEncode(body);
        var safeUrl = WebUtility.HtmlEncode(url);
        var safeApp = WebUtility.HtmlEncode(appName);
        var safeLabel = WebUtility.HtmlEncode(openLabel);

        var html = $"""
            <!doctype html>
            <html lang="{language}" dir="{direction}">
            <body style="margin:0;padding:24px;background:#f3f7fa;font-family:'IBM Plex Sans Arabic',Tahoma,Arial,sans-serif;color:#06384a;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:14px;overflow:hidden;">
                <tr><td style="background:#0083a0;color:#ffffff;padding:16px 24px;font-size:16px;font-weight:700;">{safeApp}</td></tr>
                <tr><td style="padding:24px;">
                  <h1 style="margin:0 0 12px;font-size:20px;">{safeTitle}</h1>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.7;color:#4f6470;">{safeBody}</p>
                  <a href="{safeUrl}" style="display:inline-block;padding:10px 20px;background:#0083a0;color:#ffffff;border-radius:8px;text-decoration:none;font-weight:600;">{safeLabel}</a>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(to, title, html, $"{title}\n\n{body}\n\n{url}");
    }
}
