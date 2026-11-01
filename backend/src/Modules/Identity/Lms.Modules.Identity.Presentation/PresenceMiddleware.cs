using Lms.BuildingBlocks.Application;
using Lms.Modules.Identity.Application;
using Microsoft.AspNetCore.Http;

namespace Lms.Modules.Identity.Presentation;

/// <summary>Notes that a signed-in person is active. Any request counts, including the page's own polling.</summary>
public sealed class PresenceMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ICurrentUser user, PresenceTracker presence)
    {
        if (user.Id is { } id)
        {
            presence.Touch(id);
        }

        return next(context);
    }
}
