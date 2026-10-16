using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Answers a provisional caller at an action for registered members with 403 problem details
/// carrying the stable code registration_required. Any other exception is left to the next handler.
/// The same answer is written for a provisional caller that reaches a route declared for registered
/// members only.
/// </summary>
public sealed class RegistrationRequiredExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public const string Code = "registration_required";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RegistrationRequiredException)
        {
            return false;
        }

        return await WriteAsync(httpContext, problemDetailsService, exception);
    }

    /// <summary>
    /// Writes the 403 problem details with the code registration_required.
    /// </summary>
    public static async ValueTask<bool> WriteAsync(
        HttpContext httpContext, IProblemDetailsService problemDetailsService, Exception? exception)
    {
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Registration required",
            Detail = "This action needs a registered account.",
            Extensions = { ["code"] = Code },
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
