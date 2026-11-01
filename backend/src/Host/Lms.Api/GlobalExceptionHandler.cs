using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api;

/// <summary>Turns unexpected failures into problem details without exposing internals.</summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // The browser left before the answer was ready. Nothing went wrong, and nobody is waiting for a reply.
        if (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (status, code, title) = exception switch
        {
            DbUpdateConcurrencyException => (409, "common.concurrency", "The record was changed by someone else."),
            DbUpdateException => (409, "common.conflict", "The change conflicts with existing data."),
            _ => (500, "common.unexpected", "An unexpected error occurred."),
        };

        if (status == 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }
}
