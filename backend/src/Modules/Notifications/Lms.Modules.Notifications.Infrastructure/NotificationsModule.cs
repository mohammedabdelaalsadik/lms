using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Notifications.Application;
using Lms.Modules.Notifications.Contracts;
using Lms.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace Lms.Modules.Notifications.Infrastructure;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, TimeProvider clock)
    : ModuleDbContext(options, clock), INotificationsUnitOfWork
{
    public const string SchemaName = "notifications";

    protected override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("Notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).HasConversion(id => id.Value, value => new NotificationId(value)).ValueGeneratedNever();
            b.Property(n => n.Kind).HasMaxLength(40).IsUnicode(false);
            b.Property(n => n.DedupeKey).HasMaxLength(150).IsUnicode(false);
            b.HasIndex(n => n.DedupeKey).IsUnique();
            b.Property(n => n.TitleAr).HasMaxLength(Notification.TitleMaxLength);
            b.Property(n => n.TitleEn).HasMaxLength(Notification.TitleMaxLength);
            b.Property(n => n.BodyAr).HasMaxLength(Notification.BodyMaxLength);
            b.Property(n => n.BodyEn).HasMaxLength(Notification.BodyMaxLength);
            b.Property(n => n.Link).HasMaxLength(300).IsUnicode(false);
            b.Property(n => n.EmailStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            b.HasIndex(n => new { n.RecipientId, n.CreatedAt });
            b.HasIndex(n => new { n.EmailStatus, n.NextEmailAttemptAt });
        });
    }
}

internal sealed class NotificationRepository(NotificationsDbContext db) : INotificationRepository
{
    public Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken) =>
        db.Notifications.AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken);

    public Task<Notification?> FindAsync(NotificationId id, CancellationToken cancellationToken) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListDueEmailsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.Notifications
            .Where(n => n.EmailStatus == EmailStatus.Pending && n.NextEmailAttemptAt <= now)
            .OrderBy(n => n.NextEmailAttemptAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientId, CancellationToken cancellationToken) =>
        await db.Notifications.Where(n => n.RecipientId == recipientId && n.ReadAt == null).ToListAsync(cancellationToken);

    public async Task<PagedResult<Notification>> ListAsync(Guid recipientId, bool onlyUnread, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == recipientId);

        if (onlyUnread)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Notification>(items, total, page, pageSize);
    }

    public Task<int> CountUnreadAsync(Guid recipientId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.RecipientId == recipientId && n.ReadAt == null, cancellationToken);

    public void Add(Notification notification) => db.Notifications.Add(notification);
}

/// <summary>
/// Sends through SMTP, or writes .eml files to a folder when no provider is set up.
/// Both travel the same code path, so moving to real mail is a settings change.
/// </summary>
internal sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Email");
        var from = section["From"] ?? throw new InvalidOperationException("Email:From is required.");

        using var mail = new MailMessage(from, message.To)
        {
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.Text,
            BodyEncoding = Encoding.UTF8,
        };

        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Html, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient();

        if (string.Equals(section["Mode"], "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.Host = section["Host"] ?? throw new InvalidOperationException("Email:Host is required.");
            client.Port = int.TryParse(section["Port"], out var port) ? port : 587;
            client.EnableSsl = !string.Equals(section["UseSsl"], "false", StringComparison.OrdinalIgnoreCase);
            client.Timeout = 15000;

            if (section["UserName"] is { Length: > 0 } user)
            {
                client.Credentials = new NetworkCredential(user, section["Password"]);
            }
        }
        else
        {
            var folder = Path.GetFullPath(section["PickupDirectory"] ?? throw new InvalidOperationException("Email:PickupDirectory is required."));
            Directory.CreateDirectory(folder);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = folder;
        }

        await client.SendMailAsync(mail, cancellationToken);
    }
}

internal sealed class EmailDeliveryService(IServiceScopeFactory scopes, ILogger<EmailDeliveryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().DeliverDueAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Email delivery pass failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("Lms"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.SchemaName)));

        var baseUrl = configuration["Email:AppBaseUrl"] ?? throw new InvalidOperationException("Email:AppBaseUrl is required.");

        services.AddSingleton(new EmailSettings(baseUrl));
        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<EmailDispatcher>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDeliveryService>();
        services.AddSingleton<DirectMailer>();
        services.AddSingleton<IDirectMailer>(sp => sp.GetRequiredService<DirectMailer>());
        services.AddHostedService(sp => sp.GetRequiredService<DirectMailer>());
        services.AddScoped<Lms.BuildingBlocks.Application.IPersonalDataSource, NotificationsPersonalData>();
        services.AddModuleApplication(typeof(INotificationRepository).Assembly);
        return services;
    }
}
