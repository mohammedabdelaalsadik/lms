namespace Lms.Modules.Audit.Domain;

/// <summary>One recorded action. It has no way to change after creation, and the database refuses edits too.</summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 80;
    public const int DetailsMaxLength = 2000;

    private AuditEntry()
    {
    }

    public Guid Id { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public Guid? ActorId { get; private init; }
    public string? ActorRole { get; private init; }
    public string? IpAddress { get; private init; }
    public string Action { get; private init; } = null!;
    public string Details { get; private init; } = null!;
    public bool Succeeded { get; private init; }
    public string? ErrorCode { get; private init; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        Guid? actorId,
        string? actorRole,
        string? ipAddress,
        string action,
        string details,
        bool succeeded,
        string? errorCode) => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorId = actorId,
            ActorRole = actorRole,
            IpAddress = ipAddress,
            Action = action,
            Details = details,
            Succeeded = succeeded,
            ErrorCode = errorCode,
        };
}
