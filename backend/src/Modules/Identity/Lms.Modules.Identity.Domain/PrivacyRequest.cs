using Lms.SharedKernel;

namespace Lms.Modules.Identity.Domain;

public readonly record struct PrivacyRequestId(Guid Value);

public enum PrivacyRequestKind { Export, Erasure }

public enum PrivacyRequestStatus { Pending, Approved, Rejected, Cancelled }

public sealed record PrivacyRequested(Guid RequestId, PrivacyRequestKind Kind) : IDomainEvent;

public sealed record PrivacyRequestDecided(Guid RequestId, Guid UserId) : IDomainEvent;

public static class PrivacyErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.privacy_not_found", "Request was not found.");
    public static readonly Error AlreadyOpen = Error.Conflict("identity.privacy_already_open", "You already have a request of this kind waiting.");
    public static readonly Error NotPending = Error.Conflict("identity.privacy_not_pending", "This request has already been dealt with.");
    public static readonly Error OwnRequest = Error.Conflict("identity.privacy_own_request", "Another administrator must decide your own request.");
    public static readonly Error StaffErasure = Error.Conflict("identity.privacy_staff_erasure", "Staff accounts are closed by an administrator, not through this request.");
    public static readonly Error NoteRequired = Error.Validation("identity.privacy_note_required", "Give a reason for refusing.");
    public static readonly Error ExportUnavailable = Error.Conflict("identity.privacy_export_unavailable", "This download is not available. Ask for a new copy.");
}

/// <summary>A person's request for a copy of their data, or to have their account erased. A second person decides it.</summary>
public sealed class PrivacyRequest : AggregateRoot<PrivacyRequestId>
{
    public const int TextMaxLength = 500;
    public static readonly TimeSpan DownloadLifetime = TimeSpan.FromDays(7);

    private PrivacyRequest()
    {
    }

    public Guid UserId { get; private set; }
    public PrivacyRequestKind Kind { get; private set; }
    public PrivacyRequestStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    public DateTimeOffset? DownloadUntil => Kind == PrivacyRequestKind.Export && Status == PrivacyRequestStatus.Approved ? DecidedAt + DownloadLifetime : null;

    public static Result<PrivacyRequest> Create(Guid userId, PrivacyRequestKind kind, Role role, string? reason, DateTimeOffset now)
    {
        if (kind == PrivacyRequestKind.Erasure && role != Role.Trainee)
        {
            return PrivacyErrors.StaffErasure;
        }

        var request = new PrivacyRequest
        {
            Id = new PrivacyRequestId(Guid.CreateVersion7()),
            UserId = userId,
            Kind = kind,
            Status = PrivacyRequestStatus.Pending,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            RequestedAt = now,
        };

        request.Raise(new PrivacyRequested(request.Id.Value, kind));
        return request;
    }

    public bool CanDownload(DateTimeOffset now) => DownloadUntil > now;

    public Result Approve(Guid reviewer, DateTimeOffset now)
    {
        var allowed = CheckDecision(reviewer);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        Status = PrivacyRequestStatus.Approved;
        DecidedBy = reviewer;
        DecidedAt = now;

        // Erased accounts have nobody left to tell, and the reason may be personal.
        if (Kind == PrivacyRequestKind.Erasure)
        {
            Reason = null;
        }
        else
        {
            Raise(new PrivacyRequestDecided(Id.Value, UserId));
        }

        return Result.Success();
    }

    public Result Reject(Guid reviewer, string? note, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return PrivacyErrors.NoteRequired;
        }

        var allowed = CheckDecision(reviewer);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        Status = PrivacyRequestStatus.Rejected;
        DecidedBy = reviewer;
        DecidedAt = now;
        DecisionNote = note.Trim();
        Raise(new PrivacyRequestDecided(Id.Value, UserId));
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status != PrivacyRequestStatus.Pending)
        {
            return PrivacyErrors.NotPending;
        }

        Status = PrivacyRequestStatus.Cancelled;
        return Result.Success();
    }

    private Result CheckDecision(Guid reviewer)
    {
        if (Status != PrivacyRequestStatus.Pending)
        {
            return PrivacyErrors.NotPending;
        }

        return reviewer == UserId ? PrivacyErrors.OwnRequest : Result.Success();
    }
}
