using Cichlids.Api.Features.Auth.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Satisfied by a caller that is registered. A caller with a device token acts for a provisional
/// profile and does not satisfy it.
/// </summary>
public sealed class RegisteredRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds the registered requirement for an authenticated caller without the provisional claim.
/// </summary>
public sealed class RegisteredAuthorizationHandler : AuthorizationHandler<RegisteredRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RegisteredRequirement requirement)
    {
        if (context.User.IsRegistered())
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Answers a provisional caller that fails the registered requirement with 403 and the code
/// registration_required. Every other outcome follows the default behavior of the authorization
/// middleware.
/// </summary>
public sealed class RegistrationRequiredAuthorizationResultHandler(IProblemDetailsService problemDetailsService)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<RegisteredRequirement>().Any() == true
            && context.User.IsProvisional())
        {
            await RegistrationRequiredExceptionHandler.WriteAsync(context, problemDetailsService, exception: null);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>
/// Route declaration for actions that reach other people and therefore need a registered member.
/// </summary>
public static class RegisteredRouteExtensions
{
    /// <summary>
    /// Requires an authenticated, registered caller. A provisional caller is answered with 403 and
    /// the code registration_required before the request is bound.
    /// </summary>
    public static TBuilder RequireRegistered<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AuthorizationPolicies.Registered);
}
