using Lms.BuildingBlocks.Application;
using Lms.BuildingBlocks.Presentation;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lms.Modules.Identity.Presentation;

public sealed record ChangeLanguageRequest(Language Language);

public sealed record ChangeRoleRequest(Role Role);

public sealed record RevealRequest(string Reason);

public sealed record MfaCodeRequest(string Code);

public sealed record MfaLoginStep(bool MfaRequired, string Challenge);

public sealed record MfaEnabledResponse(CurrentUserResponse User, IReadOnlyList<string> RecoveryCodes);

public sealed record ResetMfaRequest(string Reason);

[Route("api/v1/auth")]
public sealed class AuthController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    public const string AuthRateLimit = "auth";

    // The refresh cookie is sent only to the auth endpoints, never to the rest of the API.
    private const string RefreshPath = "/api/v1/auth";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> Register(RegisterTraineeCommand command, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(command, cancellationToken));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        // With a second factor on, no cookie exists until the code is checked.
        if (result.Value.MfaChallenge is { } challenge)
        {
            return Ok(new MfaLoginStep(true, challenge));
        }

        WriteSessionCookies(result.Value.Session!);
        return Ok(result.Value.Session!.User);
    }

    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> VerifyMfa(VerifyMfaCommand command, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        WriteSessionCookies(result.Value);
        return Ok(result.Value.User);
    }

    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<IActionResult> BeginMfaSetup(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Respond(await Dispatcher.Send(new BeginMfaSetupCommand(), cancellationToken));
    }

    [HttpPost("mfa/enable")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> EnableMfa(MfaCodeRequest body, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new EnableMfaCommand(body.Code), cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        Response.Headers.CacheControl = "no-store";
        WriteSessionCookies(result.Value.Session);
        return Ok(new MfaEnabledResponse(result.Value.Session.User, result.Value.RecoveryCodes));
    }

    [HttpPost("mfa/disable")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> DisableMfa(DisableMfaCommand command, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(command, cancellationToken));

    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordCommand command, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(command, cancellationToken));

    [HttpPost("password/reset")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(command, cancellationToken));

    [HttpPost("password")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimit)]
    public async Task<IActionResult> ChangePassword(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        WriteSessionCookies(result.Value);
        return Ok(result.Value.User);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[AuthCookie.RefreshName];
        if (string.IsNullOrEmpty(token))
        {
            return Failure(UserErrors.InvalidRefreshToken);
        }

        var result = await Dispatcher.Send(new RefreshSessionCommand(token), cancellationToken);
        if (!result.IsSuccess)
        {
            ClearSessionCookies();
            return Failure(result.Error!);
        }

        WriteSessionCookies(result.Value);
        return Ok(result.Value.User);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await Dispatcher.Send(new LogoutCommand(Request.Cookies[AuthCookie.RefreshName]), cancellationToken);
        ClearSessionCookies();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new GetCurrentUserQuery(), cancellationToken));

    [HttpPut("me/language")]
    [Authorize]
    public async Task<IActionResult> ChangeLanguage(ChangeLanguageRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ChangeLanguageCommand(body.Language), cancellationToken));

    private void WriteSessionCookies(LoginResponse session)
    {
        Response.Cookies.Append(AuthCookie.Name, session.Token.Value, SessionCookie("/", session.Token.ExpiresAt));
        Response.Cookies.Append(AuthCookie.RefreshName, session.Refresh.Value, SessionCookie(RefreshPath, session.Refresh.ExpiresAt));
    }

    private void ClearSessionCookies()
    {
        Response.Cookies.Delete(AuthCookie.Name, SessionCookie("/", null));
        Response.Cookies.Delete(AuthCookie.RefreshName, SessionCookie(RefreshPath, null));
    }

    private CookieOptions SessionCookie(string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Expires = expires,
        Path = path,
    };
}

public sealed record PrivacyAskRequest(PrivacyRequestKind Kind, string? Reason);

public sealed record PrivacyRejectRequest(string Note);

[Authorize]
[Route("api/v1/privacy")]
public sealed class PrivacyController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    [HttpGet("my")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListMyPrivacyRequestsQuery(), cancellationToken));

    [HttpPost("my")]
    public async Task<IActionResult> Ask(PrivacyAskRequest body, CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new CreatePrivacyRequestCommand(body.Kind, body.Reason), cancellationToken));

    [HttpPost("my/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new CancelPrivacyRequestCommand(id), cancellationToken));

    [HttpGet("my/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await Dispatcher.Send(new DownloadMyDataQuery(id), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return result.IsSuccess ? File(result.Value.Content, "application/json", result.Value.FileName) : Failure(result.Error!);
    }

    [HttpGet("requests")]
    [Authorize(Roles = LmsRoles.UserViewers)]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListPrivacyQueueQuery(), cancellationToken));

    [HttpPost("requests/{id:guid}/approve")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ApprovePrivacyRequestCommand(id), cancellationToken));

    [HttpPost("requests/{id:guid}/reject")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> Reject(Guid id, PrivacyRejectRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new RejectPrivacyRequestCommand(id, body.Note), cancellationToken));
}

[Route("api/v1/users")]
public sealed class UsersController(IDispatcher dispatcher) : ApiController(dispatcher)
{
    private const long MaxUploadBytes = 2 * 1024 * 1024;

    [HttpGet]
    [Authorize(Roles = LmsRoles.UserViewers)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] Role? role,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        Respond(await Dispatcher.Send(new ListUsersQuery(search, role, page, pageSize), cancellationToken));

    [HttpGet("online")]
    [Authorize(Roles = LmsRoles.UserViewers)]
    public async Task<IActionResult> Online(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListOnlineUsersQuery(), cancellationToken));

    [HttpGet("instructors")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> Instructors(CancellationToken cancellationToken) =>
        Respond(await Dispatcher.Send(new ListInstructorsQuery(), cancellationToken));

    [HttpPost("{id:guid}/national-id")]
    [Authorize(Roles = LmsRoles.CatalogManagers)]
    public async Task<IActionResult> RevealNationalId(Guid id, RevealRequest body, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Respond(await Dispatcher.Send(new RevealNationalIdQuery(id, body.Reason), cancellationToken));
    }

    [HttpPost("upload")]
    [Authorize(Roles = LmsRoles.Admin)]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken, [FromQuery] bool save = false)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return Respond(await Dispatcher.Send(new UploadUsersCommand(content, save), cancellationToken));
    }

    [HttpPost("{id:guid}/mfa/reset")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> ResetMfa(Guid id, ResetMfaRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ResetUserMfaCommand(id, body.Reason), cancellationToken));

    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = LmsRoles.Admin)]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeRoleRequest body, CancellationToken cancellationToken) =>
        RespondNoContent(await Dispatcher.Send(new ChangeUserRoleCommand(id, body.Role), cancellationToken));
}
