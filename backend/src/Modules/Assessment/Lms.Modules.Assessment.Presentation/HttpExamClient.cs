using Lms.Modules.Assessment.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Lms.Modules.Assessment.Presentation;

/// <summary>
/// The hash covers the address the browser used. Behind a reverse proxy the server sees a different one,
/// so production sets `Assessment:Seb:BaseUrl` to the public site address.
/// </summary>
public sealed class HttpExamClient(IHttpContextAccessor accessor, IConfiguration configuration) : IExamClient
{
    private const string Header = "X-SafeExamBrowser-ConfigKeyHash";

    public string RequestUrl
    {
        get
        {
            var request = accessor.HttpContext!.Request;
            var origin = configuration["Assessment:Seb:BaseUrl"]?.TrimEnd('/') ?? $"{request.Scheme}://{request.Host}";
            return $"{origin}{request.PathBase}{request.Path}{request.QueryString}";
        }
    }

    public string? ConfigKeyHash => accessor.HttpContext?.Request.Headers[Header].FirstOrDefault();
}
