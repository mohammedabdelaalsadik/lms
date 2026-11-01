using Lms.BuildingBlocks.Application;
using Lms.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lms.BuildingBlocks.Presentation;

[ApiController]
[Produces("application/json")]
public abstract class ApiController(IDispatcher dispatcher) : ControllerBase
{
    protected IDispatcher Dispatcher { get; } = dispatcher;

    protected IActionResult Respond<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);

    protected IActionResult RespondNoContent<T>(Result<T> result) =>
        result.IsSuccess ? NoContent() : Failure(result.Error!);

    protected IActionResult Failure(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails { Status = status, Title = error.Message };
        problem.Extensions["code"] = error.Code;

        if (error is ValidationError validation)
        {
            problem.Extensions["errors"] = validation.Failures;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
