using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Library;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Tanks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Fish;

/// <summary>
/// The page of a named fish and the owner's writes to its timeline and media. Everyone who may see the tank
/// of the fish reads the page; the writes need the owner and answer every other caller like a missing fish.
/// The write routes answer with the page of the fish as the owner sees it.
/// </summary>
public static partial class FishEndpoints
{
    public const string RequestInvalidCode = "fish_request_invalid";
    public const string AuthRequiredCode = "fish_auth_required";

    /// <summary>
    /// Whether a request path is one of the fish routes, so framework refusals such as binding errors or a
    /// missing login on these routes carry a fish problem code.
    /// </summary>
    public static bool IsFishPath(PathString path) => path.HasValue && FishPath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/fish/[0-9]+(/(growth|spawning|move|death|media(/[0-9]+)?|events/[0-9]+))?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex FishPath();

    public static IEndpointRouteBuilder MapFishEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fish/{id:long}").WithTags("Fish");
        group.MapGet("", GetAsync).WithName("GetFish").ProducesProblem(400).ProducesProblem(404);
        group.MapPost("/growth", GrowthAsync).WithName("RecordFishGrowth").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/spawning", SpawningAsync).WithName("RecordFishSpawning").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/move", MoveAsync).WithName("MoveFish").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/death", DeathAsync).WithName("RecordFishDeath").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/events/{eventId:long}", DeleteEventAsync).WithName("DeleteFishEvent").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/media", LinkMediaAsync).WithName("LinkFishMedia").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/media/{mediaItemId:long}", UnlinkMediaAsync).WithName("UnlinkFishMedia").RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Ok<FishDto>, ProblemHttpResult>> GetAsync(
        long id, string? date, ClaimsPrincipal user, TankAccessService access, StreakService streaks, FishService fish, TimeProvider clock,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (date is not null)
        {
            if (streaks.ParseLocalTime(date, null) is not var (local, _)) return Problem(FishWriteService.DateInvalidCode);
            today = local;
        }

        var page = await fish.GetAsync(id, await access.ViewerProfileIdAsync(user, ct), today, ct);
        return page is null ? Problem(FishService.NotFoundCode) : TypedResults.Ok(page);
    }

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> GrowthAsync(
        long id, RecordGrowthRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, today) => writes.RecordGrowthAsync(id, profile, request, today, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> SpawningAsync(
        long id, RecordSpawningRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, today) => writes.RecordSpawningAsync(id, profile, request, today, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> MoveAsync(
        long id, MoveFishRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, today) => writes.MoveAsync(id, profile, request, today, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> DeathAsync(
        long id, RecordDeathRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, today) => writes.RecordDeathAsync(id, profile, request, today, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> DeleteEventAsync(
        long id, long eventId, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, _) => writes.DeleteEventAsync(id, profile, eventId, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> LinkMediaAsync(
        long id, LinkFishMediaRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, _) => writes.LinkMediaAsync(id, profile, request, ct));

    private static Task<Results<Ok<FishDto>, ProblemHttpResult>> UnlinkMediaAsync(
        long id, long mediaItemId, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks,
        FishWriteService writes, FishService fish, CancellationToken ct) =>
        WriteAsync(id, date, user, profiles, streaks, fish, ct, (profile, _) => writes.UnlinkMediaAsync(id, profile, mediaItemId, ct));

    // Every write needs the local date of the caller; a write that succeeded answers with the page for its owner.
    private static async Task<Results<Ok<FishDto>, ProblemHttpResult>> WriteAsync(
        long id, string? date, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks, FishService fish, CancellationToken ct,
        Func<long, DateOnly, Task<string?>> write)
    {
        if (streaks.ParseLocalTime(date, null) is not var (today, _)) return Problem(FishWriteService.DateInvalidCode);
        var profile = await profiles.ResolveCallerAsync(user, ct);
        if (await write(profile.Id, today) is { } error) return Problem(error);
        var page = await fish.GetAsync(id, profile.Id, today, ct);
        return page is null ? Problem(FishService.NotFoundCode) : TypedResults.Ok(page);
    }

    internal static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code switch
        {
            FishService.NotFoundCode or MediaLibraryService.NotFoundCode => 404,
            FishWriteService.RemovedCode or FishWriteService.EventFixedCode or MediaLibraryService.AlreadyLinkedCode => 409,
            _ => 400,
        },
        "Fish request refused", code switch
        {
            FishService.NotFoundCode => "This fish is unavailable.",
            FishWriteService.DateInvalidCode => "Send your local calendar date as YYYY-MM-DD and a day that is not after it and not before the fish moved in.",
            FishWriteService.InvalidCode => "Check the request: a length is positive and in cm or inch, a spawning has a generation from 1 to 99 or is a hybrid, a file is named by its id.",
            FishWriteService.MoveTankInvalidCode => "Send the id of another live tank of yours.",
            FishWriteService.RemovedCode => "This fish has left the stocking.",
            FishWriteService.EventFixedCode => "Only growth and spawning entries can be deleted.",
            MediaLibraryService.NotFoundCode => "This file does not exist in your media library or is not linked to the fish.",
            MediaLibraryService.AlreadyLinkedCode => "This file is already shown on the page of the fish.",
            _ => "Check the fish request.",
        }, code);
}
