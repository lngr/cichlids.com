using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Uploads;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Care;

public static class WaterChangeEndpoints
{
    public static IEndpointRouteBuilder MapWaterChangeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks/{tankId:long}/water-changes").WithTags("Care").RequireAuthorization();
        group.MapPost("", StartAsync).WithName("StartWaterChange")
            .ProducesProblem(401).ProducesProblem(404);
        group.MapGet("/current", CurrentAsync).WithName("GetCurrentWaterChange")
            .ProducesProblem(401).ProducesProblem(404);
        group.MapPut("/{id:long}/steps/{step}", StepAsync).WithName("MarkWaterChangeStep")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        // Callers authenticate with a bearer token, never a cookie, so antiforgery validation has nothing to protect.
        group.MapPut("/{id:long}/photos/{slot}", PhotoAsync).WithName("SetWaterChangePhoto").DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadsEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: UploadsEndpoints.MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(429);
        group.MapPost("/{id:long}/finish", FinishAsync).WithName("FinishWaterChange")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);
        group.MapDelete("/{id:long}", CancelAsync).WithName("CancelWaterChange")
            .ProducesProblem(401).ProducesProblem(404);
        return app;
    }

    private static async Task<Results<Ok<WaterChangeSessionDto>, Created<WaterChangeSessionDto>, ProblemHttpResult>> StartAsync(
        long tankId, ClaimsPrincipal user, CurrentProfileService profiles, WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (start, error) = await service.StartAsync(tankId, profile.Id, ct);
        if (error is not null) return CareEndpoints.Problem(error);
        return start!.Created
            ? TypedResults.Created($"/api/tanks/{tankId}/water-changes/current", start.Session)
            : TypedResults.Ok(start.Session);
    }

    private static async Task<Results<Ok<WaterChangeSessionDto>, ProblemHttpResult>> CurrentAsync(
        long tankId, ClaimsPrincipal user, CurrentProfileService profiles, WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (session, error) = await service.CurrentAsync(tankId, profile.Id, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(session!);
    }

    private static async Task<Results<Ok<WaterChangeSessionDto>, ProblemHttpResult>> StepAsync(
        long tankId, long id, string step, WaterChangeStepRequest request, ClaimsPrincipal user, CurrentProfileService profiles,
        WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (session, error) = await service.MarkStepAsync(tankId, id, profile.Id, step, request.State, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(session!);
    }

    private static async Task<Results<Ok<WaterChangeSessionDto>, ProblemHttpResult>> PhotoAsync(
        long tankId, long id, string slot, IFormFile? file, [FromForm] bool? capturedInApp, ClaimsPrincipal user,
        CurrentProfileService profiles, WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (session, error) = await service.SetPhotoAsync(tankId, id, profile.Id, slot, file, capturedInApp ?? false, ct);
        if (error == "upload_rate_limited") return UploadsEndpoints.RateLimitProblem();
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(session!);
    }

    private static async Task<Results<Ok<WaterChangeFinishedDto>, ProblemHttpResult>> FinishAsync(
        long tankId, long id, FinishWaterChangeRequest? request, ClaimsPrincipal user, CurrentProfileService profiles,
        WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (finished, error) = await service.FinishAsync(tankId, id, profile.Id, request?.Amount, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.Ok(finished!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelAsync(
        long tankId, long id, ClaimsPrincipal user, CurrentProfileService profiles, WaterChangeService service, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var error = await service.CancelAsync(tankId, id, profile.Id, ct);
        return error is not null ? CareEndpoints.Problem(error) : TypedResults.NoContent();
    }
}
