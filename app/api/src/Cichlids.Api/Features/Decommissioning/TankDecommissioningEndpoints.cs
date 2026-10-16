using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Decommissioning;

public static partial class TankDecommissioningEndpoints
{
    public const string AuthRequiredCode = "decommission_auth_required";
    public const string RequestInvalidCode = "decommission_request_invalid";

    /// <summary>
    /// Whether a request path is one of the decommissioning routes, so framework refusals such as a
    /// missing login or a binding error on these routes carry a decommissioning problem code.
    /// </summary>
    public static bool IsDecommissioningPath(PathString path) => path.HasValue && DecommissioningPath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/tanks/[0-9]+/decommissioning(/end)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecommissioningPath();

    public static IEndpointRouteBuilder MapTankDecommissioningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tanks/{tankId:long}/decommissioning", ReadAsync).WithName("GetTankOperation").WithTags("Tanks")
            .ProducesProblem(400).ProducesProblem(404);
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        app.MapPost("/api/tanks/{tankId:long}/decommissioning", StartAsync).WithName("DecommissionTank").WithTags("Tanks")
            .RequireAuthorization().DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        app.MapPost("/api/tanks/{tankId:long}/decommissioning/end", EndAsync).WithName("RecommissionTank").WithTags("Tanks")
            .RequireAuthorization().DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
        return app;
    }

    private static async Task<Results<Ok<TankOperationDto>, ProblemHttpResult>> ReadAsync(
        long tankId, string? date, int? utcOffsetMinutes, ClaimsPrincipal user, TankAccessService access, StreakService streaks,
        TankDecommissioningService decommissioning, CancellationToken ct)
    {
        if (await access.FindAsync(tankId, user, ct) is not { CanSeeStream: true } found) return Problem(TankDecommissioningService.NotFoundCode);
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(TankDecommissioningService.DateInvalidCode);
        return TypedResults.Ok(await decommissioning.ReadAsync(tankId, found.Profile, today, offset, ct));
    }

    private static async Task<Results<Created<TankOperationDto>, ProblemHttpResult>> StartAsync(
        long tankId, IFormFile? file, [FromForm] bool? capturedInApp, [FromForm] string? scope, string? date, int? utcOffsetMinutes,
        ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks, TankDecommissioningService decommissioning, CancellationToken ct)
    {
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(TankDecommissioningService.DateInvalidCode);
        var profile = await profiles.ResolveAsync(user, ct);
        var (operation, error) = await decommissioning.StartAsync(tankId, profile.Id, file, capturedInApp ?? false, scope, today, offset, ct);
        return error is not null ? Problem(error) : TypedResults.Created($"/api/tanks/{tankId}/decommissioning", operation!);
    }

    private static async Task<Results<Ok<TankOperationDto>, ProblemHttpResult>> EndAsync(
        long tankId, IFormFile? file, [FromForm] bool? capturedInApp, string? date, int? utcOffsetMinutes,
        ClaimsPrincipal user, CurrentProfileService profiles, StreakService streaks, TankDecommissioningService decommissioning, CancellationToken ct)
    {
        if (streaks.ParseLocalTime(date, utcOffsetMinutes) is not var (today, offset)) return Problem(TankDecommissioningService.DateInvalidCode);
        var profile = await profiles.ResolveAsync(user, ct);
        var (operation, error) = await decommissioning.EndAsync(tankId, profile.Id, file, capturedInApp ?? false, today, offset, ct);
        return error is not null ? Problem(error) : TypedResults.Ok(operation!);
    }

    private static ProblemHttpResult Problem(string code)
    {
        if (code == UploadsEndpoints.UploadRateLimitedCode) return UploadsEndpoints.RateLimitProblem();
        return ApiProblems.WithCode(
            code switch
            {
                TankDecommissioningService.NotFoundCode => 404,
                TankDecommissioningService.ActiveCode or TankDecommissioningService.NotActiveCode => 409,
                _ => 400,
            },
            "Decommissioning request refused", code switch
            {
                TankDecommissioningService.NotFoundCode => "This tank is unavailable.",
                TankDecommissioningService.DateInvalidCode => "Send your local calendar date as YYYY-MM-DD and, optionally, the offset of your clock from UTC in minutes (-840 to 840); a tank is put back into operation on or after the day it was decommissioned.",
                TankDecommissioningService.ScopeInvalidCode => "Send scope all_care or water_change.",
                TankDecommissioningService.ActiveCode => "This tank is already decommissioned.",
                TankDecommissioningService.NotActiveCode => "This tank is not decommissioned.",
                TankDecommissioningService.PhotoNotInAppCode => "The photo has to be taken with the camera of the app.",
                TankDecommissioningService.PhotoTimeCode => "The capture time of the photo does not match the moment it was sent.",
                TankDecommissioningService.PhotoReusedCode => "This photo has already served as proof; take a new one.",
                _ => "Send one JPEG, PNG or WebP photo of up to 25 MB that can be decoded.",
            }, code);
    }
}
