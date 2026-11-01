using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.Modules.Notifications.Contracts;
using Lms.SharedKernel;

namespace Lms.Modules.Identity.Application;

/// <summary>Always succeeds, so the page cannot be used to find out who has an account.</summary>
public sealed record ForgotPasswordCommand(string NationalId) : ICommand<Unit>
{
    public override string ToString() => nameof(ForgotPasswordCommand);
}

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(c => c.NationalId).NotEmpty().MaximumLength(20);
}

internal sealed class ForgotPasswordHandler(
    IUserRepository users,
    INationalIdProtector protector,
    ITokenService tokens,
    IDirectMailer mailer,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var nationalId = NationalId.Create(request.NationalId);
        var user = nationalId.IsSuccess ? await users.FindByLookupHashAsync(protector.ComputeLookupHash(nationalId.Value), cancellationToken) : null;

        if (user is not null)
        {
            var token = tokens.IssueRefreshToken();
            user.BeginPasswordReset(token.Hash, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Queued, so answering takes the same time whether or not the account exists.
            mailer.QueuePasswordReset(user.Email, user.Language == Language.Ar, $"/reset-password?token={Uri.EscapeDataString(token.Value)}");
        }

        return default(Unit);
    }
}

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<Unit>
{
    public override string ToString() => nameof(ResetPasswordCommand);
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(200);
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

internal sealed class ResetPasswordHandler(
    IUserRepository users,
    IPasswordService passwords,
    ITokenService tokens,
    IRefreshTokenRepository refreshTokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var user = await users.FindByResetHashAsync(tokens.HashRefreshToken(request.Token), cancellationToken);

        if (user is null || !user.ResetIsValid(now))
        {
            return UserErrors.InvalidResetLink;
        }

        user.CompletePasswordReset(passwords.Hash(request.NewPassword));
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
