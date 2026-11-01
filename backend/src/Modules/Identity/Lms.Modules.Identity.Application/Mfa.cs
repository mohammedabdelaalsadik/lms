using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Modules.Identity.Application;

public static class IdentityApplication
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services) => services.AddScoped<SessionStarter>();
}

public interface IMfaPolicy
{
    bool RequiredForStaff { get; }
}

public interface IMfaSecrets
{
    byte[] NewSecret();

    string Protect(byte[] secret);

    byte[] Reveal(string cipher);

    IReadOnlyList<string> NewRecoveryCodes();

    string HashRecoveryCode(string code);

    string QrDataUrl(string text);
}

public sealed record LoginOutcome(LoginResponse? Session, string? MfaChallenge, Guid UserId) : IAuditActor
{
    Guid IAuditActor.ActorId => UserId;
}

/// <summary>Starts a session once every required factor has been checked.</summary>
internal sealed class SessionStarter(IRefreshTokenRepository refreshTokens, ITokenService tokens, IMfaPolicy policy, TimeProvider clock)
{
    public LoginResponse Start(User user)
    {
        var refresh = tokens.IssueRefreshToken();
        refreshTokens.Add(RefreshToken.Issue(user.Id, refresh.Hash, Guid.CreateVersion7(), clock.GetUtcNow(), refresh.ExpiresAt));
        return new LoginResponse(user.ToCurrentUser(policy), tokens.Issue(user), refresh);
    }
}

public sealed record VerifyMfaCommand(string Challenge, string Code) : ICommand<LoginResponse>
{
    public override string ToString() => nameof(VerifyMfaCommand);
}

internal sealed class VerifyMfaValidator : AbstractValidator<VerifyMfaCommand>
{
    public VerifyMfaValidator()
    {
        RuleFor(c => c.Challenge).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
    }
}

internal sealed class VerifyMfaHandler(
    IUserRepository users,
    ITokenService tokens,
    IMfaSecrets secrets,
    SessionStarter sessions,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<VerifyMfaCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(VerifyMfaCommand request, CancellationToken cancellationToken)
    {
        var userId = await tokens.ReadMfaChallengeAsync(request.Challenge);
        var user = userId is null ? null : await users.FindAsync(new UserId(userId.Value), cancellationToken);
        var now = clock.GetUtcNow();

        if (user is null || !user.MfaEnabled || user.IsLockedOut(now))
        {
            return UserErrors.InvalidMfaCode;
        }

        var step = Totp.Match(secrets.Reveal(user.MfaSecretCipher!), request.Code, now, user.MfaLastStep);

        if (step is { } accepted)
        {
            user.RecordMfaStep(accepted);
        }
        else if (!user.UseRecoveryCode(secrets.HashRecoveryCode(request.Code)))
        {
            // Wrong codes count toward the same lockout as wrong passwords.
            user.RecordFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return UserErrors.InvalidMfaCode;
        }

        user.RecordSuccessfulLogin();
        var session = sessions.Start(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }
}

public sealed record MfaSetupResponse(string Secret, string QrDataUrl);

public sealed record BeginMfaSetupCommand : ICommand<MfaSetupResponse>;

internal sealed class BeginMfaSetupHandler(IUserRepository users, IMfaSecrets secrets, ICurrentUser current, IIdentityUnitOfWork unitOfWork)
    : IRequestHandler<BeginMfaSetupCommand, MfaSetupResponse>
{
    private const string Issuer = "LMS";

    public async Task<Result<MfaSetupResponse>> Handle(BeginMfaSetupCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(current.Id!.Value), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var secret = secrets.NewSecret();
        var started = user.BeginMfaSetup(secrets.Protect(secret));
        if (!started.IsSuccess)
        {
            return started.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var text = Totp.ToBase32(secret);
        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        return new MfaSetupResponse(text, secrets.QrDataUrl($"otpauth://totp/{label}?secret={text}&issuer={Issuer}&digits=6&period=30"));
    }
}

public sealed record EnableMfaResult(LoginResponse Session, IReadOnlyList<string> RecoveryCodes);

public sealed record EnableMfaCommand(string Code) : ICommand<EnableMfaResult>
{
    public override string ToString() => nameof(EnableMfaCommand);
}

internal sealed class EnableMfaValidator : AbstractValidator<EnableMfaCommand>
{
    public EnableMfaValidator() => RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
}

internal sealed class EnableMfaHandler(
    IUserRepository users,
    IMfaSecrets secrets,
    IRefreshTokenRepository refreshTokens,
    SessionStarter sessions,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<EnableMfaCommand, EnableMfaResult>
{
    public async Task<Result<EnableMfaResult>> Handle(EnableMfaCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(current.Id!.Value), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.MfaEnabled)
        {
            return UserErrors.MfaAlreadyEnabled;
        }

        if (user.MfaSecretCipher is null)
        {
            return UserErrors.InvalidMfaCode;
        }

        var now = clock.GetUtcNow();
        var step = Totp.Match(secrets.Reveal(user.MfaSecretCipher), request.Code, now, user.MfaLastStep);
        if (step is null)
        {
            return UserErrors.InvalidMfaCode;
        }

        var codes = secrets.NewRecoveryCodes();
        user.CompleteMfaSetup(step.Value, [.. codes.Select(secrets.HashRecoveryCode)], now);

        // Sessions from before the second factor end here. This one replaces them.
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        var session = sessions.Start(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EnableMfaResult(session, codes);
    }
}

public sealed record DisableMfaCommand(string Password, string Code) : ICommand<Unit>
{
    public override string ToString() => nameof(DisableMfaCommand);
}

internal sealed class DisableMfaValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaValidator()
    {
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Code).NotEmpty().MaximumLength(20);
    }
}

internal sealed class DisableMfaHandler(
    IUserRepository users,
    IPasswordService passwords,
    IMfaSecrets secrets,
    IMfaPolicy policy,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<DisableMfaCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DisableMfaCommand request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(current.Id!.Value), cancellationToken);
        if (user is null || !user.MfaEnabled)
        {
            return UserErrors.NotFound;
        }

        if (policy.RequiredForStaff && user.Role != Role.Trainee)
        {
            return UserErrors.MfaRequired;
        }

        var now = clock.GetUtcNow();
        var step = Totp.Match(secrets.Reveal(user.MfaSecretCipher!), request.Code, now, user.MfaLastStep);

        if (!passwords.Verify(user.PasswordHash, request.Password) || step is null)
        {
            return UserErrors.InvalidMfaCode;
        }

        user.ClearMfa();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

/// <summary>For someone who lost both their phone and recovery codes. Requires a reason and ends their sessions.</summary>
public sealed record ResetUserMfaCommand(Guid UserId, string Reason) : ICommand<Unit>
{
    public override string ToString() => nameof(ResetUserMfaCommand);
}

internal sealed class ResetUserMfaValidator : AbstractValidator<ResetUserMfaCommand>
{
    public ResetUserMfaValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500).Matches(@"\S");
}

internal sealed class ResetUserMfaHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser current,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<ResetUserMfaCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ResetUserMfaCommand request, CancellationToken cancellationToken)
    {
        // A second admin must do this, so one stolen session cannot remove its own second factor.
        if (current.Id == request.UserId)
        {
            return UserErrors.CannotResetOwnMfa;
        }

        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.ClearMfa();
        await refreshTokens.RevokeAllForUserAsync(user.Id, clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}
