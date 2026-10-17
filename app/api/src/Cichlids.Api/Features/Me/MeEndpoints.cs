using System.Security.Claims;
using System.Globalization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Retrospectives;
using Cichlids.Api.Features.Streaks;
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

        app.MapGet("/api/me/today/followed", GetTodayFollowedAsync)
            .WithName("GetTodayFollowed")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/me/followed-tanks", ListFollowedTanksAsync)
            .WithName("ListMyFollowedTanks")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/me/today/milestones", GetTodayMilestonesAsync)
            .WithName("GetTodayMilestones")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPut("/api/me/today/milestones/{key}/dismissal", DismissMilestoneAsync)
            .WithName("DismissMilestone")
            .WithTags("Me")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<Ok<TodayFollowedDto>> GetTodayFollowedAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, FollowedTanksService followed, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        return TypedResults.Ok(await followed.GetTodayAsync(profile.Id, cancellationToken));
    }

    private static async Task<Ok<PagedResponse<FollowedTankDto>>> ListFollowedTanksAsync(
        int? offset, int? limit, ClaimsPrincipal user, CurrentProfileService profiles, FollowedTanksService followed, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        return TypedResults.Ok(await followed.ListAsync(profile.Id, normalizedOffset, normalizedLimit, cancellationToken));
    }

    private static async Task<Results<Ok<MilestonesDto>, ProblemHttpResult>> GetTodayMilestonesAsync(
        string? date, int? utcOffsetMinutes, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks, MilestoneService milestones,
        CancellationToken cancellationToken)
    {
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset))
            return ApiProblems.WithCode(400, "Invalid date", "Send your local calendar date as YYYY-MM-DD and, optionally, the offset of your clock from UTC in minutes (-840 to 840).", "today_invalid_date");
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        return TypedResults.Ok(await milestones.ListAsync(profile.Id, today, offset, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DismissMilestoneAsync(
        string key, ClaimsPrincipal user, CurrentProfileService profiles, MilestoneService milestones, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveCallerAsync(user, cancellationToken);
        return await milestones.DismissAsync(profile.Id, key, cancellationToken) switch
        {
            MilestoneService.KeyInvalidCode => ApiProblems.WithCode(400, "Invalid milestone", "The milestone key is not valid.", MilestoneService.KeyInvalidCode),
            MilestoneService.NotFoundCode => ApiProblems.WithCode(404, "Milestone not found", "The milestone belongs to no tank of yours.", MilestoneService.NotFoundCode),
            _ => TypedResults.NoContent(),
        };
    }

    private static async Task<Ok<MeDto>> GetAsync(
        ClaimsPrincipal user,
        CurrentProfileService currentProfileService,
        ProfilesQueryService profilesQueryService,
        ArchiveRetrospectiveQueryService retrospectives,
        ReturnRetrospectiveService returnRetrospectives,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveCallerAsync(user, cancellationToken);

        var detail = await profilesQueryService.GetOwnAsync(profile, cancellationToken)
            ?? throw new InvalidOperationException($"The resolved profile {profile.Id} has no public detail view.");

        var roles = user.Identity is ClaimsIdentity identity
            ? identity.FindAll(identity.RoleClaimType).Select(c => c.Value).Distinct().ToList()
            : [];

        var provisional = user.IsProvisional();
        var archiveRetrospective = provisional
            ? null
            : new ArchiveRetrospectiveStatusDto(
                await retrospectives.ExistsAsync(profile.Id, cancellationToken), profile.ArchiveRetrospectiveSeenAt);

        return TypedResults.Ok(new MeDto(
            detail, roles, provisional, HandleChangeService.NextChangeAt(profile, clock.GetUtcNow()), archiveRetrospective,
            provisional ? null : await returnRetrospectives.GetStatusAsync(profile.Id, cancellationToken)));
    }
}
