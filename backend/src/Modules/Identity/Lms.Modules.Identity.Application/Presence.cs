using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using System.Collections.Concurrent;

namespace Lms.Modules.Identity.Application;

/// <summary>
/// Who has been active lately. Held in memory on purpose: it is a live view, not a record of anyone's
/// habits, so nothing is stored. With more than one server, each sees only its own visitors.
/// </summary>
public sealed class PresenceTracker(TimeProvider clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seen = new();

    public void Touch(Guid userId) => _seen[userId] = clock.GetUtcNow();

    public IReadOnlyList<(Guid UserId, DateTimeOffset LastSeenAt)> Online()
    {
        var cutoff = clock.GetUtcNow() - Window;

        foreach (var stale in _seen.Where(s => s.Value < cutoff))
        {
            _seen.TryRemove(stale.Key, out _);
        }

        return [.. _seen.OrderByDescending(s => s.Value).Select(s => (s.Key, s.Value))];
    }
}

public sealed record OnlineUser(NameDto Name, string Role, DateTimeOffset LastSeenAt);

public sealed record ListOnlineUsersQuery : IQuery<IReadOnlyList<OnlineUser>>;

internal sealed class ListOnlineUsersHandler(PresenceTracker presence, IUserRepository users) : IRequestHandler<ListOnlineUsersQuery, IReadOnlyList<OnlineUser>>
{
    private const int MaxShown = 200;

    public async Task<Result<IReadOnlyList<OnlineUser>>> Handle(ListOnlineUsersQuery request, CancellationToken cancellationToken)
    {
        var items = new List<OnlineUser>();

        foreach (var (userId, lastSeen) in presence.Online().Take(MaxShown))
        {
            var user = await users.FindAsync(new UserId(userId), cancellationToken);
            if (user is { IsErased: false })
            {
                items.Add(new OnlineUser(new NameDto(user.FullName.Ar, user.FullName.En), user.Role.ToString(), lastSeen));
            }
        }

        return Result<IReadOnlyList<OnlineUser>>.Success(items);
    }
}
