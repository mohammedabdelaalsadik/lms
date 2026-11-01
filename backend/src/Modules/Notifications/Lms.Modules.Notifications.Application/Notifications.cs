using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Catalog.Contracts;
using Lms.Modules.Identity.Contracts;
using Lms.Modules.Notifications.Contracts;
using Lms.Modules.Notifications.Domain;
using Lms.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lms.Modules.Notifications.Application;

public interface INotificationsUnitOfWork : IUnitOfWork;

public interface INotificationRepository
{
    Task<bool> ExistsAsync(string dedupeKey, CancellationToken cancellationToken);

    Task<Notification?> FindAsync(NotificationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Notification>> ListDueEmailsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientId, CancellationToken cancellationToken);

    Task<PagedResult<Notification>> ListAsync(Guid recipientId, bool onlyUnread, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid recipientId, CancellationToken cancellationToken);

    void Add(Notification notification);
}

public sealed class Notifier(
    INotificationRepository notifications,
    IUserDirectory directory,
    ICourseCatalog catalog,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider clock) : INotifier
{
    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        if (await notifications.ExistsAsync(request.DedupeKey, cancellationToken))
        {
            return;
        }

        var recipients = await directory.GetAsync([request.RecipientId], cancellationToken);
        if (!recipients.ContainsKey(request.RecipientId))
        {
            return;
        }

        CourseInfo? course = null;

        if (request.CourseId is { } courseId)
        {
            var courses = await catalog.GetAsync([courseId], cancellationToken);
            course = courses.GetValueOrDefault(courseId);
        }

        var text = NotificationTemplates.Render(request.Kind, course?.TitleAr, course?.TitleEn, request.NoteAr, request.NoteEn);
        notifications.Add(Notification.Create(request.RecipientId, request.Kind.ToString(), request.DedupeKey, text, request.Link, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record EmailSettings(string AppBaseUrl);

/// <summary>Sends what is due. One failed address never holds up the others.</summary>
public sealed class EmailDispatcher(
    INotificationRepository notifications,
    IUserDirectory directory,
    IEmailSender sender,
    INotificationsUnitOfWork unitOfWork,
    EmailSettings settings,
    TimeProvider clock,
    ILogger<EmailDispatcher> logger)
{
    private const int BatchSize = 20;

    public async Task DeliverDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await notifications.ListDueEmailsAsync(now, BatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        var people = await directory.GetAsync([.. due.Select(n => n.RecipientId).Distinct()], cancellationToken);

        foreach (var notification in due)
        {
            if (!people.TryGetValue(notification.RecipientId, out var person) || string.IsNullOrWhiteSpace(person.Email))
            {
                notification.AbandonEmail();
                continue;
            }

            try
            {
                await sender.SendAsync(Compose(notification, person), cancellationToken);
                notification.RecordEmailSent(clock.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                notification.RecordEmailFailure(clock.GetUtcNow());
                logger.LogWarning(exception, "Email for notification {NotificationId} failed on attempt {Attempt}", notification.Id.Value, notification.EmailAttempts);
            }

            // Saved each time, so a crash midway can't send anything twice.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private EmailMessage Compose(Notification notification, UserInfo person)
    {
        var arabic = !string.Equals(person.Language, "En", StringComparison.OrdinalIgnoreCase);
        var url = $"{settings.AppBaseUrl.TrimEnd('/')}{notification.Link}";

        return EmailComposer.Compose(
            person.Email,
            arabic,
            arabic ? notification.TitleAr : notification.TitleEn,
            arabic ? notification.BodyAr : notification.BodyEn,
            url,
            arabic ? "منصة التدريب" : "Training Platform",
            arabic ? "فتح المنصة" : "Open the platform");
    }
}

public sealed record TextDto(string Ar, string En);

public sealed record NotificationResponse(Guid Id, string Kind, TextDto Title, TextDto Body, string Link, DateTimeOffset CreatedAt, bool IsRead);

public sealed record ListMyNotificationsQuery(bool OnlyUnread, int Page = 1, int PageSize = 20) : IQuery<PagedResult<NotificationResponse>>;

internal sealed class ListMyNotificationsValidator : AbstractValidator<ListMyNotificationsQuery>
{
    public ListMyNotificationsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class ListMyNotificationsHandler(INotificationRepository notifications, ICurrentUser user)
    : IRequestHandler<ListMyNotificationsQuery, PagedResult<NotificationResponse>>
{
    public async Task<Result<PagedResult<NotificationResponse>>> Handle(ListMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        var page = await notifications.ListAsync(user.Id!.Value, request.OnlyUnread, request.Page, request.PageSize, cancellationToken);

        return new PagedResult<NotificationResponse>(
            [.. page.Items.Select(n => new NotificationResponse(
                n.Id.Value,
                n.Kind,
                new TextDto(n.TitleAr, n.TitleEn),
                new TextDto(n.BodyAr, n.BodyEn),
                n.Link,
                n.CreatedAt,
                n.ReadAt is not null))],
            page.Total,
            page.Page,
            page.PageSize);
    }
}

public sealed record UnreadCountResponse(int Count);

public sealed record GetUnreadCountQuery : IQuery<UnreadCountResponse>;

internal sealed class GetUnreadCountHandler(INotificationRepository notifications, ICurrentUser user)
    : IRequestHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken) =>
        new UnreadCountResponse(await notifications.CountUnreadAsync(user.Id!.Value, cancellationToken));
}

public sealed record MarkNotificationReadCommand(Guid Id) : ICommand<Unit>, INotAudited;

internal sealed class MarkNotificationReadHandler(
    INotificationRepository notifications,
    ICurrentUser user,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<MarkNotificationReadCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var notification = await notifications.FindAsync(new NotificationId(request.Id), cancellationToken);

        // Someone else's notification answers exactly like a missing one.
        if (notification is null || notification.RecipientId != user.Id)
        {
            return NotificationErrors.NotFound;
        }

        notification.MarkRead(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record MarkAllNotificationsReadCommand : ICommand<Unit>, INotAudited;

internal sealed class MarkAllNotificationsReadHandler(
    INotificationRepository notifications,
    ICurrentUser user,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<MarkAllNotificationsReadCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var notification in await notifications.ListUnreadAsync(user.Id!.Value, cancellationToken))
        {
            notification.MarkRead(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
