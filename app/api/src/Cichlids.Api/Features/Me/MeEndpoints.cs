using System.Security.Claims;
using System.Globalization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.Devices;
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

        app.MapPut("/api/me/handle", ChangeHandleAsync)
            .WithName("ChangeMyHandle")
            .WithTags("Me")
            .RequireRegistered()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<Results<Ok<HandleChangeDto>, ProblemHttpResult>> ChangeHandleAsync(
        ChangeHandleRequest? request, ClaimsPrincipal user, HttpContext http, CurrentProfileService profiles,
        HandleChangeService handles, TimeProvider clock, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var result = await handles.ChangeAsync(profile.Id, request?.Handle, cancellationToken);
        switch (result.Outcome)
        {
            case HandleChangeOutcome.Changed:
            case HandleChangeOutcome.Unchanged:
                return TypedResults.Ok(new HandleChangeDto(result.Handle!, result.NextChangeAt));
            case HandleChangeOutcome.Generated:
                return ApiProblems.WithCode(400, "Handle not available", "Names of the form user plus eight digits, guest_ plus a number and provisional- are generated for members and cannot be chosen.", HandleChangeService.GeneratedCode);
            case HandleChangeOutcome.Reserved:
                return ApiProblems.WithCode(400, "Handle reserved", "This name is reserved for the platform.", HandleChangeService.ReservedCode);
            case HandleChangeOutcome.Taken:
                return ApiProblems.WithCode(409, "Handle taken", "Another member uses this handle or has it reserved.", HandleChangeService.TakenCode);
            case HandleChangeOutcome.TooSoon:
                var next = result.NextChangeAt!.Value;
                http.Response.Headers.RetryAfter =
                    Math.Max(1, (long)Math.Ceiling((next - clock.GetUtcNow()).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return ApiProblems.WithCode(429, "Handle changed recently", "The handle changes once per 30 days.", HandleChangeService.TooSoonCode,
                    new Dictionary<string, object?> { ["nextChangeAt"] = next });
            default:
                return ApiProblems.WithCode(400, "Invalid handle", "Use 3 to 30 characters: letters a to z, digits, underscore and dot, starting with a letter and ending with a letter or digit, without two dots in a row.", HandleChangeService.InvalidCode);
        }
    }

    private static async Task<Ok<MeSettingsDto>> GetSettingsAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        return TypedResults.Ok(new MeSettingsDto(profile.StreaksEnabled));
    }

    private static async Task<Results<Ok<MeSettingsDto>, ProblemHttpResult>> UpdateSettingsAsync(
        UpdateMeSettingsRequest? request, ClaimsPrincipal user, CurrentProfileService profiles, CichlidsDbContext context,
        CancellationToken cancellationToken)
    {
        if (request?.StreaksEnabled is not { } streaksEnabled)
            return ApiProblems.WithCode(400, "Invalid settings", "Send streaksEnabled as true or false.", "settings_invalid");
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
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
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        return TypedResults.Ok(await today.GetAsync(profile, localDate, cancellationToken));
    }

    private static async Task<Ok<MeDto>> GetAsync(
        ClaimsPrincipal user,
        CurrentProfileService currentProfileService,
        ProfilesQueryService profilesQueryService,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveCallerAsync(user, cancellationToken);

        var detail = await profilesQueryService.GetOwnAsync(profile, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profile.Id} has no public detail view.");

        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];

        return TypedResults.Ok(new MeDto(
            detail, roles, user.IsProvisional(), HandleChangeService.NextChangeAt(profile, clock.GetUtcNow())));
    }
}
