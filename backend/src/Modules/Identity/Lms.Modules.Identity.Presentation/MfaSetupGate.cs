using Microsoft.AspNetCore.Http;

namespace Lms.Modules.Identity.Presentation;

/// <summary>Staff who still owe a second factor can reach only the sign-in endpoints until they set it up.</summary>
public sealed class MfaSetupGate(RequestDelegate next)
{
    private const string Claim = "mfa_setup";
    private const string OpenPath = "/api/v1/auth";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(c => c.Type == Claim) && !context.Request.Path.StartsWithSegments(OpenPath))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "identity.mfa_setup_required" });
            return;
        }

        await next(context);
    }
}
