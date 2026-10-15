using System.Security.Claims;
using System.Globalization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Me;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", GetAsync)
            .WithName("GetMe")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/me/today", GetTodayAsync)
            .WithName("GetToday")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<TodayDto>, ProblemHttpResult>> GetTodayAsync(
        ClaimsPrincipal user, string? date, CurrentProfileService profiles, TodayQueryService today,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            return ApiProblems.WithCode(400, "Invalid date", "Send your local calendar date as YYYY-MM-DD.", "today_invalid_date");
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return TypedResults.Ok(await today.GetAsync(profile, localDate, cancellationToken));
    }

    private static async Task<Ok<MeDto>> GetAsync(
        ClaimsPrincipal user,
        CurrentProfileService currentProfileService,
        ProfilesQueryService profilesQueryService,
        CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveAsync(user, cancellationToken);

        var detail = await profilesQueryService.GetByIdAsync(profile.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profile.Id} has no public detail view.");

        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];

        return TypedResults.Ok(new MeDto(detail, roles));
    }
}
