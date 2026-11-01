using Lms.Modules.Notifications.Application;
using Lms.Modules.Notifications.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Lms.Modules.Notifications.Infrastructure;

/// <summary>
/// Sends secret links straight out. They stay in memory only, never in the notifications table,
/// so a restart can lose one, and the person simply asks again.
/// </summary>
internal sealed class DirectMailer(IEmailSender sender, EmailSettings settings, ILogger<DirectMailer> logger) : BackgroundService, IDirectMailer
{
    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void QueuePasswordReset(string email, bool arabic, string link)
    {
        var url = $"{settings.AppBaseUrl.TrimEnd('/')}{link}";

        _queue.Writer.TryWrite(EmailComposer.Compose(
            email,
            arabic,
            arabic ? "إعادة تعيين كلمة المرور" : "Reset your password",
            arabic ? "يعمل هذا الرابط مرة واحدة خلال 30 دقيقة. إذا لم تطلبه فتجاهل هذه الرسالة." : "This link works once, for 30 minutes. If you didn't ask for it, ignore this email.",
            url,
            arabic ? "منصة التدريب" : "Training Platform",
            arabic ? "اختيار كلمة مرور جديدة" : "Choose a new password"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await sender.SendAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError("Password reset email could not be sent: {Reason}", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
