using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Answers a login bound to a non-member profile with 403 problem details carrying the stable
/// code non_member_profile. Any other exception is left to the next handler.
/// </summary>
public sealed class NonMemberLoginExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public const string Code = "non_member_profile";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not NonMemberLoginException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Login not usable",
            Detail = "This account is bound to a profile that is not a member profile and cannot be used to sign in.",
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
