using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Streaks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Tanks;

public static partial class InhabitantEndpoints
{
    public const string RequestInvalidCode = "inhabitant_request_invalid";
    public const string AuthRequiredCode = "inhabitant_auth_required";
    public const string DateInvalidCode = "inhabitant_date_invalid";

    /// <summary>
    /// Whether a request path is one of the stocking routes, so framework refusals such as binding
    /// errors or a missing login on these routes carry a stocking problem code.
    /// </summary>
    public static bool IsInhabitantPath(PathString path) => path.HasValue && InhabitantPath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/tanks/[0-9]+/inhabitants(/[0-9]+(/removal)?)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex InhabitantPath();

    public static IEndpointRouteBuilder MapInhabitantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks/{tankId:long}/inhabitants").WithTags("Tanks");
        group.MapGet("", ListAsync).WithName("ListTankInhabitants").ProducesProblem(404);
        group.MapPost("", AddAsync).WithName("AddTankInhabitant").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        group.MapPut("/{id:long}", UpdateAsync).WithName("UpdateTankInhabitant").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/{id:long}/removal", RemoveAsync).WithName("RemoveTankInhabitant").RequireAuthorization()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409);
        return app;
    }

    private static async Task<Results<Ok<InhabitantListDto>, ProblemHttpResult>> ListAsync(
        long tankId, ClaimsPrincipal user, TankAccessService access, InhabitantService inhabitants, CancellationToken ct)
    {
        var list = await inhabitants.ListAsync(tankId, await access.ViewerProfileIdAsync(user, ct), ct);
        return list is null ? Problem(InhabitantService.NotFoundCode) : TypedResults.Ok(list);
    }

    private static async Task<Results<Created<InhabitantDto>, ProblemHttpResult>> AddAsync(
        long tankId, AddInhabitantRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles,
        StreakService streaks, InhabitantService inhabitants, CancellationToken ct)
    {
        if (streaks.ParseLocalTime(date, null) is not var (today, _)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (inhabitant, error) = await inhabitants.AddAsync(tankId, profile.Id, request, today, ct);
        return error is not null ? Problem(error) : TypedResults.Created($"/api/tanks/{tankId}/inhabitants", inhabitant!);
    }

    private static async Task<Results<Ok<InhabitantDto>, ProblemHttpResult>> UpdateAsync(
        long tankId, long id, UpdateInhabitantRequest? request, string? date, ClaimsPrincipal user, CurrentProfileService profiles,
        StreakService streaks, InhabitantService inhabitants, CancellationToken ct)
    {
        if (streaks.ParseLocalTime(date, null) is not var (today, _)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (inhabitant, error) = await inhabitants.UpdateAsync(tankId, id, profile.Id, request, today, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(inhabitant!);
    }

    private static async Task<Results<Ok<InhabitantDto>, ProblemHttpResult>> RemoveAsync(
        long tankId, long id, RemoveInhabitantRequest? request, string? date, int? utcOffsetMinutes, ClaimsPrincipal user,
        CurrentProfileService profiles, StreakService streaks, InhabitantService inhabitants, CancellationToken ct)
    {
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(DateInvalidCode);
        var profile = await profiles.ResolveCallerAsync(user, ct);
        var (inhabitant, error) = await inhabitants.RemoveAsync(tankId, id, profile.Id, request, today, offset, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(inhabitant!);
    }

    internal static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code switch
        {
            InhabitantService.NotFoundCode => 404,
            InhabitantService.RemovedCode => 409,
            _ => 400,
        },
        "Stocking request refused", code switch
        {
            InhabitantService.NotFoundCode => "This tank or inhabitant is unavailable.",
            DateInvalidCode => "Send your local calendar date as YYYY-MM-DD.",
            InhabitantService.InvalidCode => "A group needs a species and a count from 1 to 10000; a named fish needs a name of up to 80 characters.",
            InhabitantService.SpeciesUnknownCode => "This species is not in the catalog.",
            InhabitantService.MovedInInvalidCode => "Send the moved-in day as YYYY-MM-DD, not after your local date.",
            InhabitantService.ReasonInvalidCode => "Send the reason as died, given_away or moved.",
            InhabitantService.RemovedOnInvalidCode => "Send the removal day as YYYY-MM-DD, not after your local date and not before the day the inhabitant moved in.",
            InhabitantService.RemovedCode => "This inhabitant has already left the stocking.",
            InhabitantService.MovedInAfterEventsCode => "The moved-in day cannot be after the first entry on the timeline of the fish.",
            InhabitantService.NameRequiredCode => "A named fish keeps its name.",
            _ => "Check the stocking request.",
        }, code);
}
