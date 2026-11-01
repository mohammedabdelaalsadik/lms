using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Identity.Application;

public sealed record NewRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken cancellationToken);

    void Add(RefreshToken token);

    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record RefreshSessionCommand(string Token) : ICommand<LoginResponse>, INotAudited
{
    public override string ToString() => nameof(RefreshSessionCommand);
}

internal sealed class RefreshSessionHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ITokenService tokens,
    IMfaPolicy policy,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RefreshSessionCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stored = await refreshTokens.FindByHashAsync(tokens.HashRefreshToken(request.Token), cancellationToken);

        if (stored is null)
        {
            return UserErrors.InvalidRefreshToken;
        }

        if (stored.IsReuse(now))
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return UserErrors.InvalidRefreshToken;
        }

        if (!stored.IsActive(now))
        {
            return UserErrors.InvalidRefreshToken;
        }

        var user = await users.FindAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return UserErrors.InvalidRefreshToken;
        }

        stored.Rotate(now);
        var next = tokens.IssueRefreshToken();
        refreshTokens.Add(RefreshToken.Issue(user.Id, next.Hash, stored.FamilyId, now, next.ExpiresAt));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResponse(user.ToCurrentUser(policy), tokens.Issue(user), next);
    }
}

public sealed record LogoutCommand(string? Token) : ICommand<Unit>
{
    public override string ToString() => nameof(LogoutCommand);
}

internal sealed class LogoutHandler(
    IRefreshTokenRepository refreshTokens,
    ITokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Token))
        {
            var stored = await refreshTokens.FindByHashAsync(tokens.HashRefreshToken(request.Token), cancellationToken);

            if (stored is not null)
            {
                await refreshTokens.RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow(), cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        return default(Unit);
    }
}

public sealed record ChangeUserRoleCommand(Guid UserId, Role Role) : ICommand<Unit>;

internal sealed class ChangeUserRoleValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleValidator() => RuleFor(c => c.Role).IsInEnum();
}

internal sealed class ChangeUserRoleHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ChangeUserRoleCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        // Stops an admin from removing their own access by accident.
        if (current.Id == request.UserId)
        {
            return UserErrors.CannotChangeOwnRole;
        }

        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.ChangeRole(request.Role);

        // Forces a new sign-in so the old role cannot be renewed.
        await refreshTokens.RevokeAllForUserAsync(user.Id, clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record UserOption(Guid Id, NameDto FullName);

public sealed record ListInstructorsQuery : IQuery<IReadOnlyList<UserOption>>;

internal sealed class ListInstructorsHandler(IUserRepository users) : IRequestHandler<ListInstructorsQuery, IReadOnlyList<UserOption>>
{
    public async Task<Result<IReadOnlyList<UserOption>>> Handle(ListInstructorsQuery request, CancellationToken cancellationToken)
    {
        var options = await users.ListByRoleAsync(Role.Instructor, cancellationToken);
        return Result<IReadOnlyList<UserOption>>.Success(options);
    }
}
