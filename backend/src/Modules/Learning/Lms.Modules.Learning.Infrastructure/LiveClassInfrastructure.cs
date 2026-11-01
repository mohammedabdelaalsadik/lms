using Lms.BuildingBlocks.Infrastructure;
using Lms.Modules.Enrollment.Contracts;
using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Lms.Modules.Notifications.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Xml.Linq;

namespace Lms.Modules.Learning.Infrastructure;

internal sealed class LiveClassRepository(LearningDbContext db) : ILiveClassRepository
{
    public Task<LiveClass?> FindAsync(LiveClassId id, CancellationToken cancellationToken) =>
        db.LiveClasses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LiveClass>> ListAsync(Guid cohortId, CancellationToken cancellationToken) =>
        await db.LiveClasses.AsNoTracking().Where(c => c.CohortId == cohortId).OrderByDescending(c => c.StartsAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LiveAttendance>> ListAttendanceAsync(IReadOnlyCollection<LiveClassId> ids, CancellationToken cancellationToken) =>
        await db.LiveAttendance.AsNoTracking().Where(a => ids.Contains(a.LiveClassId)).OrderBy(a => a.JoinedAt).ToListAsync(cancellationToken);

    public Task<bool> HasAttendedAsync(LiveClassId id, Guid traineeId, CancellationToken cancellationToken) =>
        db.LiveAttendance.AnyAsync(a => a.LiveClassId == id && a.TraineeId == traineeId, cancellationToken);

    public void Add(LiveClass live) => db.LiveClasses.Add(live);

    public void Add(LiveAttendance attendance) => db.LiveAttendance.Add(attendance);

    public void Remove(LiveClass live) => db.LiveClasses.Remove(live);
}

/// <summary>Set `LiveClasses:BaseUrl` and `LiveClasses:Secret` to connect. Without them, the feature says it is not connected.</summary>
internal sealed class BigBlueButtonServer(IConfiguration configuration, ILogger<BigBlueButtonServer> logger) : ILiveClassServer
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    private string? BaseUrl => configuration["LiveClasses:BaseUrl"];

    private string? Secret => configuration["LiveClasses:Secret"];

    private string Algorithm => configuration["LiveClasses:ChecksumAlgorithm"] ?? "sha256";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Secret);

    public Task<bool> OpenAsync(LiveMeeting meeting, CancellationToken cancellationToken)
    {
        List<(string, string)> parameters =
        [
            ("meetingID", meeting.MeetingId),
            ("name", meeting.Name),
            ("duration", meeting.DurationMinutes.ToString()),
            ("record", "false"),
        ];

        if (configuration["LiveClasses:ReturnUrl"] is { Length: > 0 } back)
        {
            parameters.Add(("logoutURL", back));
        }

        return CallAsync("create", parameters, cancellationToken);
    }

    public string JoinUrl(string meetingId, string fullName, Guid userId, bool moderator) => Url("join",
    [
        ("meetingID", meetingId),
        ("fullName", fullName),
        ("role", moderator ? "MODERATOR" : "VIEWER"),
        ("userID", userId.ToString("N")),
        ("redirect", "true"),
    ]);

    public Task<bool> CloseAsync(string meetingId, CancellationToken cancellationToken) =>
        CallAsync("end", [("meetingID", meetingId)], cancellationToken);

    private string Url(string call, IEnumerable<(string, string)> parameters) => BigBlueButtonUrl.Build(BaseUrl!, Secret!, Algorithm, call, parameters);

    private async Task<bool> CallAsync(string call, IEnumerable<(string, string)> parameters, CancellationToken cancellationToken)
    {
        try
        {
            var body = await Client.GetStringAsync(Url(call, parameters), cancellationToken);
            var code = XDocument.Parse(body).Root?.Element("returncode")?.Value;

            if (code != "SUCCESS")
            {
                // Signed addresses stay out of the log. Only the call name and the server's own code go in.
                logger.LogWarning("The live class server refused {Call}: {MessageKey}", call, XDocument.Parse(body).Root?.Element("messageKey")?.Value);
            }

            return code == "SUCCESS";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            logger.LogWarning("The live class server could not be reached for {Call}: {Error}", call, exception.GetType().Name);
            return false;
        }
    }
}

internal sealed class LiveClassScheduledProcessor(IServiceScopeFactory scopes, ILogger<LiveClassScheduledProcessor> logger)
    : OutboxProcessor<LearningDbContext>(scopes, logger)
{
    protected override string EventType => typeof(LiveClassScheduled).FullName!;

    protected override async Task HandleAsync(IServiceProvider services, string payload, CancellationToken cancellationToken)
    {
        var scheduled = JsonSerializer.Deserialize<LiveClassScheduled>(payload)!;
        var notifier = services.GetRequiredService<INotifier>();

        foreach (var trainee in await services.GetRequiredService<ICohortDirectory>().GetEnrolledTraineesAsync(scheduled.CohortId, cancellationToken))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(trainee, NotificationKind.LiveClassScheduled, $"live:{scheduled.LiveClassId}:{trainee}", null, scheduled.TitleAr, scheduled.TitleEn, $"/live/{scheduled.CohortId}"),
                cancellationToken);
        }
    }
}
