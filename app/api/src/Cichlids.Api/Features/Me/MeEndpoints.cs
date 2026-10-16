using System.Security.Claims;
using System.Globalization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Profiles;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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

        app.MapGet("/api/me/settings", GetSettingsAsync)
            .WithName("GetMySettings")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPut("/api/me/settings", UpdateSettingsAsync)
            .WithName("UpdateMySettings")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Ok<MeSettingsDto>> GetSettingsAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return TypedResults.Ok(new MeSettingsDto(profile.StreaksEnabled));
    }

    private static async Task<Results<Ok<MeSettingsDto>, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateMeSettingsRequest? request, ClaimsPrincipal user, CurrentProfileService profiles, CichlidsDbContext context,
        CancellationToken cancellationToken)
    {
        if (request?.StreaksEnabled is not { } streaksEnabled)
            return ApiProblems.WithCode(400, "Invalid settings", "Send streaksEnabled as true or false.", "settings_invalid");
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        await context.Profiles.Where(p => p.Id == profile.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StreaksEnabled, streaksEnabled), cancellationToken);
        return TypedResults.Ok(new MeSettingsDto(streaksEnabled));
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

        var detail = await profilesQueryService.GetByIdAsync(profile.Id, profile.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profile.Id} has no public detail view.");

        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];

        return TypedResults.Ok(new MeDto(detail, roles));
    }
}
