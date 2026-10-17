using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Care;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Me;

public static class RetrospectiveEndpoints
{
    public static IEndpointRouteBuilder MapRetrospectiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/retrospectives/return", GetReturnAsync)
            .WithName("GetReturnRetrospective")
            .WithTags("Me")
            .RequireRegistered()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/me/retrospectives/return/seen", MarkReturnSeenAsync)
            .WithName("MarkReturnRetrospectiveSeen")
            .WithTags("Me")
            .RequireRegistered()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<ReturnRetrospectiveDto>, NoContent, ProblemHttpResult>> GetReturnAsync(
        string? date, int? utcOffsetMinutes, ClaimsPrincipal user, CurrentProfileService profiles, ReturnRetrospectiveService retrospectives,
        CancellationToken cancellationToken)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return CareEndpoints.Problem("care_date_invalid");
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return await retrospectives.GetAsync(profile.Id, today, offset, cancellationToken) is { } retrospective
            ? TypedResults.Ok(retrospective)
            : TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkReturnSeenAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, ReturnRetrospectiveService retrospectives, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        await retrospectives.MarkSeenAsync(profile.Id, cancellationToken);
        return TypedResults.NoContent();
    }
}
