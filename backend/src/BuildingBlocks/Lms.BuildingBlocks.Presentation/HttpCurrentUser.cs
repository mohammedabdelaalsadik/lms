using Lms.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.BuildingBlocks.Presentation;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id => Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? Role => accessor.HttpContext?.User.FindFirst("role")?.Value;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public static class PresentationRegistration
{
    public static IServiceCollection AddLmsPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
