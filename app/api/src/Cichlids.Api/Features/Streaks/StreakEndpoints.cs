using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Tanks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Streaks;

public static partial class StreakEndpoints
{
    public const string RequestInvalidCode = "streak_request_invalid";
    public const string AuthRequiredCode = "streak_auth_required";
    public const string DateInvalidCode = "streak_date_invalid";

    /// <summary>
    /// Whether a request path is one of the streak or vacation routes, so framework refusals such as
    /// binding errors or a missing login on these routes carry a streak problem code.
    /// </summary>
    public static bool IsStreakPath(PathString path) => path.HasValue && StreakPath().IsMatch(path.Value!);

    [GeneratedRegex("^(/api/tanks/[0-9]+/streaks|/api/me/streaks|/api/me/vacations|/api/me/vacations/active/end)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex StreakPath();

    public static IEndpointRouteBuilder MapStreakEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tanks/{tankId:long}/streaks", TankAsync).WithName("GetTankStreaks").WithTags("Streaks")
            .ProducesProblem(400).ProducesProblem(404);
        app.MapGet("/api/me/streaks", OwnAsync).WithName("GetMyStreaks").WithTags("Streaks").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        var vacations = app.MapGroup("/api/me/vacations").WithTags("Streaks").RequireAuthorization();
        vacations.MapGet("", ListVacationsAsync).WithName("ListMyVacations").ProducesProblem(401).ProducesProblem(403);
        vacations.MapPost("", StartVacationAsync).WithName("StartVacation")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        vacations.MapPost("/active/end", EndVacationAsync).WithName("EndVacation")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Ok<TankStreaksDto>, ProblemHttpResult>> TankAsync(
        long tankId, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, TankAccessService access, StreakService streaks, CancellationToken ct)
    {
        if (await access.FindAsync(tankId, user, ct) is not { CanSeeProfile: true } found) return Problem(StreakService.NotFoundCode);
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(DateInvalidCode);
        return TypedResults.Ok(await streaks.GetTankAsync(found, today, offset, ct));
    }

    private static async Task<Results<Ok<MeStreaksDto>, ProblemHttpResult>> OwnAsync(
        string? date, int? utcOffsetMinutes, ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveAsync(user, ct);
        return TypedResults.Ok(await streaks.GetOwnAsync(profile.Id, today, offset, ct));
    }

    private static async Task<Ok<VacationListDto>> ListVacationsAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, VacationService vacations, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        return TypedResults.Ok(new VacationListDto(await vacations.ListAsync(profile.Id, ct)));
    }

    private static async Task<Results<Created<VacationDto>, ProblemHttpResult>> StartVacationAsync(
        StartVacationRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles, VacationService vacations, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, null) is not var (today, _)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveAsync(user, ct);
        var (vacation, error) = await vacations.StartAsync(profile.Id, request, today, ct);
        return error is not null ? Problem(error) : TypedResults.Created("/api/me/vacations", vacation!);
    }

    private static async Task<Results<Ok<VacationDto>, ProblemHttpResult>> EndVacationAsync(
        string? date, ClaimsPrincipal user, CurrentProfileService profiles, VacationService vacations, CancellationToken ct)
    {
        if (CarePlanService.ParseLocalTime(date, null) is not var (today, _)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveAsync(user, ct);
        var (vacation, error) = await vacations.EndAsync(profile.Id, today, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(vacation!);
    }

    internal static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code switch
        {
            StreakService.NotFoundCode or VacationService.NotFoundCode => 404,
            VacationService.ActiveCode or VacationService.OverlapCode => 409,
            _ => 400,
        },
        "Streak request refused", code switch
        {
            StreakService.NotFoundCode => "This tank is unavailable.",
            DateInvalidCode => "Send your local calendar date as YYYY-MM-DD and, optionally, the offset of your clock from UTC in minutes (-840 to 840).",
            VacationService.NotFoundCode => "There is no active vacation.",
            VacationService.DatesInvalidCode => "A vacation starts and ends on or before today, and it does not end before it starts; a vacation recorded afterwards needs both dates.",
            VacationService.ActiveCode => "A vacation is already active.",
            VacationService.OverlapCode => "This vacation overlaps another vacation.",
            _ => "Check the streak or vacation request.",
        }, code);
}
