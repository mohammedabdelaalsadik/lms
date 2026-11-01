using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using System.Text.Json;

namespace Lms.Modules.Identity.Application;

public interface IPrivacyRequestRepository
{
    Task<PrivacyRequest?> FindAsync(PrivacyRequestId id, CancellationToken cancellationToken);

    Task<bool> HasPendingAsync(Guid userId, PrivacyRequestKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivacyRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivacyRequest>> ListRecentAsync(int take, CancellationToken cancellationToken);

    void Add(PrivacyRequest request);
}

public sealed record PrivacyRequestResponse(
    Guid Id,
    PrivacyRequestKind Kind,
    PrivacyRequestStatus Status,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? DownloadUntil);

public sealed record PrivacyQueueItem(PrivacyRequestResponse Request, NameDto Person, string Role);

internal static class PrivacyMapping
{
    public static PrivacyRequestResponse ToResponse(this PrivacyRequest r) =>
        new(r.Id.Value, r.Kind, r.Status, r.Reason, r.RequestedAt, r.DecidedAt, r.DecisionNote, r.DownloadUntil);
}

public sealed record ListMyPrivacyRequestsQuery : IQuery<IReadOnlyList<PrivacyRequestResponse>>;

internal sealed class ListMyPrivacyRequestsHandler(IPrivacyRequestRepository requests, ICurrentUser user) : IRequestHandler<ListMyPrivacyRequestsQuery, IReadOnlyList<PrivacyRequestResponse>>
{
    public async Task<Result<IReadOnlyList<PrivacyRequestResponse>>> Handle(ListMyPrivacyRequestsQuery request, CancellationToken cancellationToken)
    {
        var list = await requests.ListForUserAsync(user.Id!.Value, cancellationToken);
        IReadOnlyList<PrivacyRequestResponse> items = [.. list.Select(r => r.ToResponse())];
        return Result<IReadOnlyList<PrivacyRequestResponse>>.Success(items);
    }
}

public sealed record CreatePrivacyRequestCommand(PrivacyRequestKind Kind, string? Reason) : ICommand<Guid>;

internal sealed class CreatePrivacyRequestValidator : AbstractValidator<CreatePrivacyRequestCommand>
{
    public CreatePrivacyRequestValidator() => RuleFor(c => c.Reason).MaximumLength(PrivacyRequest.TextMaxLength);
}

internal sealed class CreatePrivacyRequestHandler(
    IPrivacyRequestRepository requests,
    IUserRepository users,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CreatePrivacyRequestCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePrivacyRequestCommand request, CancellationToken cancellationToken)
    {
        var id = current.Id!.Value;
        var user = await users.FindAsync(new UserId(id), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (await requests.HasPendingAsync(id, request.Kind, cancellationToken))
        {
            return PrivacyErrors.AlreadyOpen;
        }

        var created = PrivacyRequest.Create(id, request.Kind, user.Role, request.Reason, clock.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        requests.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Value.Id.Value;
    }
}

public sealed record CancelPrivacyRequestCommand(Guid Id) : ICommand<Unit>;

internal sealed class CancelPrivacyRequestHandler(IPrivacyRequestRepository requests, ICurrentUser user, IIdentityUnitOfWork unitOfWork) : IRequestHandler<CancelPrivacyRequestCommand, Unit>
{
    public async Task<Result<Unit>> Handle(CancelPrivacyRequestCommand request, CancellationToken cancellationToken)
    {
        var found = await requests.FindAsync(new PrivacyRequestId(request.Id), cancellationToken);
        if (found is null || found.UserId != user.Id)
        {
            return PrivacyErrors.NotFound;
        }

        var outcome = found.Cancel();
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ListPrivacyQueueQuery : IQuery<IReadOnlyList<PrivacyQueueItem>>;

internal sealed class ListPrivacyQueueHandler(IPrivacyRequestRepository requests, IUserRepository users) : IRequestHandler<ListPrivacyQueueQuery, IReadOnlyList<PrivacyQueueItem>>
{
    private const int MaxItems = 200;

    public async Task<Result<IReadOnlyList<PrivacyQueueItem>>> Handle(ListPrivacyQueueQuery request, CancellationToken cancellationToken)
    {
        var list = await requests.ListRecentAsync(MaxItems, cancellationToken);
        var items = new List<PrivacyQueueItem>();

        foreach (var entry in list)
        {
            var person = await users.FindAsync(new UserId(entry.UserId), cancellationToken);
            items.Add(new PrivacyQueueItem(
                entry.ToResponse(),
                new NameDto(person?.FullName.Ar ?? "", person?.FullName.En ?? ""),
                person?.Role.ToString() ?? ""));
        }

        return Result<IReadOnlyList<PrivacyQueueItem>>.Success(items);
    }
}

public sealed record RejectPrivacyRequestCommand(Guid Id, string Note) : ICommand<Unit>;

internal sealed class RejectPrivacyRequestValidator : AbstractValidator<RejectPrivacyRequestCommand>
{
    public RejectPrivacyRequestValidator() => RuleFor(c => c.Note).MaximumLength(PrivacyRequest.TextMaxLength);
}

internal sealed class RejectPrivacyRequestHandler(IPrivacyRequestRepository requests, ICurrentUser user, IIdentityUnitOfWork unitOfWork, TimeProvider clock) : IRequestHandler<RejectPrivacyRequestCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RejectPrivacyRequestCommand request, CancellationToken cancellationToken)
    {
        var found = await requests.FindAsync(new PrivacyRequestId(request.Id), cancellationToken);
        if (found is null)
        {
            return PrivacyErrors.NotFound;
        }

        var outcome = found.Reject(user.Id!.Value, request.Note, clock.GetUtcNow());
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record ApprovePrivacyRequestCommand(Guid Id) : ICommand<Unit>;

/// <summary>
/// Erasure runs each module first, because those steps can be repeated. The request closes in the same save
/// as the account, so a crash part way leaves it pending and an administrator can simply approve it again.
/// </summary>
internal sealed class ApprovePrivacyRequestHandler(
    IPrivacyRequestRepository requests,
    IUserRepository users,
    IRefreshTokenRepository tokens,
    IEnumerable<IPersonalDataSource> sources,
    ICurrentUser user,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ApprovePrivacyRequestCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ApprovePrivacyRequestCommand request, CancellationToken cancellationToken)
    {
        var found = await requests.FindAsync(new PrivacyRequestId(request.Id), cancellationToken);
        if (found is null)
        {
            return PrivacyErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        var outcome = found.Approve(user.Id!.Value, now);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        if (found.Kind == PrivacyRequestKind.Erasure)
        {
            var person = await users.FindAsync(new UserId(found.UserId), cancellationToken);
            if (person is null)
            {
                return UserErrors.NotFound;
            }

            foreach (var source in sources)
            {
                await source.EraseAsync(found.UserId, cancellationToken);
            }

            await tokens.RevokeAllForUserAsync(person.Id, now, cancellationToken);
            person.Erase(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record PersonalDataFile(byte[] Content, string FileName);

public sealed record DownloadMyDataQuery(Guid RequestId) : IQuery<PersonalDataFile>, IAuditedRequest;

internal sealed class DownloadMyDataHandler(
    IPrivacyRequestRepository requests,
    IEnumerable<IPersonalDataSource> sources,
    ICurrentUser user,
    TimeProvider clock) : IRequestHandler<DownloadMyDataQuery, PersonalDataFile>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<Result<PersonalDataFile>> Handle(DownloadMyDataQuery request, CancellationToken cancellationToken)
    {
        var found = await requests.FindAsync(new PrivacyRequestId(request.RequestId), cancellationToken);
        if (found is null || found.UserId != user.Id)
        {
            return PrivacyErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        if (!found.CanDownload(now))
        {
            return PrivacyErrors.ExportUnavailable;
        }

        var data = new SortedDictionary<string, object>();
        foreach (var source in sources)
        {
            var section = await source.ReadAsync(found.UserId, cancellationToken);
            data[section.Name] = section.Data;
        }

        return new PersonalDataFile(JsonSerializer.SerializeToUtf8Bytes(new { GeneratedAt = now, Sections = data }, Options), "my-data.json");
    }
}

public sealed class IdentityPersonalData(IUserRepository users, IPrivacyRequestRepository requests) : IPersonalDataSource
{
    public async Task<PersonalDataSection> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(userId), cancellationToken);
        var history = await requests.ListForUserAsync(userId, cancellationToken);

        return new PersonalDataSection("account", new
        {
            Name = user?.FullName,
            user?.Email,
            NationalId = user?.NationalId.Masked,
            Role = user?.Role.ToString(),
            Language = user?.Language.ToString(),
            user?.CreatedAt,
            PrivacyRequests = history.Select(r => new { Kind = r.Kind.ToString(), Status = r.Status.ToString(), r.RequestedAt, r.DecidedAt }),
        });
    }
}
