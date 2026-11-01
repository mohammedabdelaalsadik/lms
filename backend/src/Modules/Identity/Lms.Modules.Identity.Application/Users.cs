using FluentValidation;
using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.Modules.Identity.Application;

public interface IIdentityUnitOfWork : IUnitOfWork;

public static class AuthCookie
{
    public const string Name = "lms_access";
    public const string RefreshName = "lms_refresh";
}

public interface INationalIdProtector
{
    ProtectedNationalId Protect(NationalId nationalId);

    string ComputeLookupHash(NationalId nationalId);

    string Reveal(ProtectedNationalId nationalId);
}

public interface IPasswordService
{
    string Hash(string password);

    bool Verify(string hash, string password);

    /// <summary>Spends the same time as a real check so unknown accounts cannot be detected by timing.</summary>
    void VerifyAgainstDummy(string password);
}

public interface ITokenService
{
    AccessToken Issue(User user);

    /// <summary>A short proof that the password was right. It opens nothing except the second step.</summary>
    string IssueMfaChallenge(User user);

    Task<Guid?> ReadMfaChallengeAsync(string challenge);

    NewRefreshToken IssueRefreshToken();

    string HashRefreshToken(string value);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IUserRepository
{
    Task<User?> FindAsync(UserId id, CancellationToken cancellationToken);

    Task<User?> FindByLookupHashAsync(string lookupHash, CancellationToken cancellationToken);

    Task<User?> FindByResetHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<bool> LookupHashExistsAsync(string lookupHash, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    void Add(User user);

    Task<PagedResult<UserSummary>> ListAsync(string? search, Role? role, bool? active, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserOption>> ListByRoleAsync(Role role, CancellationToken cancellationToken);
}

public sealed record NameDto(string Ar, string En);

public sealed record UserSummary(
    Guid Id,
    string MaskedNationalId,
    NameDto FullName,
    string Email,
    Role Role,
    DateTimeOffset CreatedAt,
    bool IsActive,
    bool IsErased);

public sealed record CurrentUserResponse(
    Guid Id,
    string MaskedNationalId,
    NameDto FullName,
    string Email,
    Role Role,
    Language Language,
    bool MfaEnabled,
    bool MfaSetupRequired,
    bool MustChangePassword) : IAuditActor
{
    Guid IAuditActor.ActorId => Id;
}

public sealed record LoginResponse(CurrentUserResponse User, AccessToken Token, NewRefreshToken Refresh) : IAuditActor
{
    Guid IAuditActor.ActorId => User.Id;
}

internal static class UserMapping
{
    public static CurrentUserResponse ToCurrentUser(this User u, IMfaPolicy policy) => new(
        u.Id.Value,
        u.NationalId.Masked,
        new NameDto(u.FullName.Ar, u.FullName.En),
        u.Email,
        u.Role,
        u.Language,
        u.MfaEnabled,
        u.NeedsMfaSetup(policy.RequiredForStaff),
        u.MustChangePassword);
}

public sealed record RegisterTraineeCommand(
    string NationalId,
    string FullNameAr,
    string FullNameEn,
    string Email,
    string Password,
    Language Language) : ICommand<CurrentUserResponse>
{
    public override string ToString() => nameof(RegisterTraineeCommand);
}

internal sealed class RegisterTraineeValidator : AbstractValidator<RegisterTraineeCommand>
{
    public RegisterTraineeValidator()
    {
        RuleFor(c => c.NationalId).NotEmpty();
        RuleFor(c => c.FullNameAr).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.FullNameEn).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(c => c.Language).IsInEnum();
    }
}

internal sealed class RegisterTraineeHandler(
    IUserRepository users,
    INationalIdProtector protector,
    IPasswordService passwords,
    IMfaPolicy policy,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RegisterTraineeCommand, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(
        RegisterTraineeCommand request,
        CancellationToken cancellationToken)
    {
        var nationalId = NationalId.Create(request.NationalId);
        if (!nationalId.IsSuccess)
        {
            return nationalId.Error!;
        }

        var name = LocalizedText.Create(request.FullNameAr, request.FullNameEn, User.NameMaxLength);
        if (!name.IsSuccess)
        {
            return name.Error!;
        }

        if (await users.LookupHashExistsAsync(protector.ComputeLookupHash(nationalId.Value), cancellationToken))
        {
            return UserErrors.NationalIdTaken;
        }

        if (await users.EmailExistsAsync(User.NormalizeEmail(request.Email), cancellationToken))
        {
            return UserErrors.EmailTaken;
        }

        var user = User.Register(
            protector.Protect(nationalId.Value),
            name.Value,
            request.Email,
            passwords.Hash(request.Password),
            Role.Trainee,
            request.Language,
            clock.GetUtcNow());

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return user.ToCurrentUser(policy);
    }
}

public sealed record LoginCommand(string NationalId, string Password) : ICommand<LoginOutcome>
{
    public override string ToString() => nameof(LoginCommand);
}

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(c => c.NationalId).NotEmpty();
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class LoginHandler(
    IUserRepository users,
    INationalIdProtector protector,
    IPasswordService passwords,
    ITokenService tokens,
    SessionStarter sessions,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<LoginCommand, LoginOutcome>
{
    public async Task<Result<LoginOutcome>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var nationalId = NationalId.Create(request.NationalId);
        var user = nationalId.IsSuccess
            ? await users.FindByLookupHashAsync(protector.ComputeLookupHash(nationalId.Value), cancellationToken)
            : null;

        if (user is null)
        {
            passwords.VerifyAgainstDummy(request.Password);
            return UserErrors.InvalidCredentials;
        }

        var now = clock.GetUtcNow();

        // A locked or deactivated account answers exactly like a wrong password, so neither reveals anything.
        if (user.IsLockedOut(now) || !user.IsActive)
        {
            passwords.VerifyAgainstDummy(request.Password);
            return UserErrors.InvalidCredentials;
        }

        if (!passwords.Verify(user.PasswordHash, request.Password))
        {
            user.RecordFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return UserErrors.InvalidCredentials;
        }

        // The failure count stays until the second step passes, or code guesses could reset it.
        if (user.MfaEnabled)
        {
            return new LoginOutcome(null, tokens.IssueMfaChallenge(user), user.Id.Value);
        }

        user.RecordSuccessfulLogin();
        var session = sessions.Start(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LoginOutcome(session, null, user.Id.Value);
    }
}

public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;

internal sealed class GetCurrentUserHandler(IUserRepository users, ICurrentUser current, IMfaPolicy policy)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (current.Id is not { } id)
        {
            return UserErrors.NotFound;
        }

        var user = await users.FindAsync(new UserId(id), cancellationToken);
        return user is null ? UserErrors.NotFound : user.ToCurrentUser(policy);
    }
}

public sealed record ChangeLanguageCommand(Language Language) : ICommand<Unit>, INotAudited;

internal sealed class ChangeLanguageHandler(IUserRepository users, ICurrentUser current, IIdentityUnitOfWork unitOfWork)
    : IRequestHandler<ChangeLanguageCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ChangeLanguageCommand request, CancellationToken cancellationToken)
    {
        var user = current.Id is { } id ? await users.FindAsync(new UserId(id), cancellationToken) : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.ChangeLanguage(request.Language);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return default(Unit);
    }
}

public sealed record NationalIdResponse(string NationalId);

/// <summary>The only way to see a full National ID. A reason is required and every use is logged.</summary>
public sealed record RevealNationalIdQuery(Guid UserId, string Reason) : IQuery<NationalIdResponse>, IAuditedRequest
{
    public override string ToString() => nameof(RevealNationalIdQuery);
}

internal sealed class RevealNationalIdValidator : AbstractValidator<RevealNationalIdQuery>
{
    public RevealNationalIdValidator() => RuleFor(q => q.Reason).NotEmpty().MaximumLength(500).Matches(@"\S");
}

internal sealed class RevealNationalIdHandler(IUserRepository users, INationalIdProtector protector)
    : IRequestHandler<RevealNationalIdQuery, NationalIdResponse>
{
    public async Task<Result<NationalIdResponse>> Handle(RevealNationalIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(new UserId(request.UserId), cancellationToken);
        return user is null || user.IsErased ? UserErrors.NotFound : new NationalIdResponse(protector.Reveal(user.NationalId));
    }
}

public sealed record ListUsersQuery(string? Search, Role? Role, int Page = 1, int PageSize = 20, bool? Active = null) : IQuery<PagedResult<UserSummary>>;

internal sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

internal sealed class ListUsersHandler(IUserRepository users) : IRequestHandler<ListUsersQuery, PagedResult<UserSummary>>
{
    public async Task<Result<PagedResult<UserSummary>>> Handle(ListUsersQuery request, CancellationToken cancellationToken) =>
        await users.ListAsync(request.Search, request.Role, request.Active, request.Page, request.PageSize, cancellationToken);
}
