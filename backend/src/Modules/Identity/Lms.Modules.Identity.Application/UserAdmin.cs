using System.Security.Cryptography;
using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Identity.Application;

internal static class TemporaryPassword
{
    private const int Length = 14;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    public static string Create() => RandomNumberGenerator.GetString(Alphabet, Length);
}

public sealed record UpdateUserProfileCommand(Guid UserId, string NameAr, string NameEn, string Email) : ICommand<Unit>
{
    public override string ToString() => nameof(UpdateUserProfileCommand);
}

internal sealed class UpdateUserProfileValidator : AbstractValidator<UpdateUserProfileCommand>
{
    public UpdateUserProfileValidator()
    {
        RuleFor(c => c.NameAr).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.NameEn).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
    }
}

internal sealed class UpdateUserProfileHandler(IUserRepository users, IIdentityUnitOfWork unitOfWork) : IRequestHandler<UpdateUserProfileCommand, Unit>
{
    public async Task<Result<Unit>> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        if (user is null || user.IsErased)
        {
            return UserErrors.NotFound;
        }

        var name = LocalizedText.Create(request.NameAr, request.NameEn, User.NameMaxLength);
        if (!name.IsSuccess)
        {
            return name.Error!;
        }

        var email = User.NormalizeEmail(request.Email);
        if (email != user.Email && await users.EmailExistsAsync(email, cancellationToken))
        {
            return UserErrors.EmailTaken;
        }

        user.ChangeProfile(name.Value, email);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record SetUserActiveCommand(Guid UserId, bool Active) : ICommand<Unit>;

internal sealed class SetUserActiveHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<SetUserActiveCommand, Unit>
{
    public async Task<Result<Unit>> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        // Stops an admin from locking themselves out by accident.
        if (current.Id == request.UserId)
        {
            return UserErrors.CannotChangeOwnAccount;
        }

        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        if (user is null || user.IsErased)
        {
            return UserErrors.NotFound;
        }

        var now = clock.GetUtcNow();

        if (request.Active)
        {
            user.Reactivate();
        }
        else
        {
            user.Deactivate(now);
            await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record TemporaryPasswordResponse(string Password)
{
    public override string ToString() => nameof(TemporaryPasswordResponse);
}

/// <summary>Replaces the password with a one-time one, shown once to the admin, that must be changed at the next sign-in.</summary>
public sealed record ResetUserPasswordCommand(Guid UserId) : ICommand<TemporaryPasswordResponse>;

internal sealed class ResetUserPasswordHandler(
    IUserRepository users,
    IPasswordService passwords,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ResetUserPasswordCommand, TemporaryPasswordResponse>
{
    public async Task<Result<TemporaryPasswordResponse>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        // Admins change their own password the normal way, which asks for the current one.
        if (current.Id == request.UserId)
        {
            return UserErrors.CannotChangeOwnAccount;
        }

        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        if (user is null || user.IsErased)
        {
            return UserErrors.NotFound;
        }

        var password = TemporaryPassword.Create();
        user.CompletePasswordReset(passwords.Hash(password));
        user.RequirePasswordChange();

        await refreshTokens.RevokeAllForUserAsync(user.Id, clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new TemporaryPasswordResponse(password);
    }
}
