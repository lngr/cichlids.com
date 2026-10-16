using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Onboarding;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/me/onboarding", CompleteAsync).WithName("CompleteOnboarding").WithTags("Me").RequireRegistered();
        app.MapGet("/api/me/legacy-tanks", LegacyAsync).WithName("ListMyLegacyTanks").WithTags("Me").RequireRegistered();
        app.MapPut("/api/me/interests", InterestsAsync).WithName("SetMyInterests").WithTags("Me").RequireRegistered();
        app.MapPut("/api/tanks/{id:long}/region", RefineAsync).WithName("RefineTankRegion").WithTags("Tanks").RequireRegistered();
        return app;
    }

    private static async Task<Results<Ok<OnboardingResultDto>, ProblemHttpResult>> CompleteAsync(
        OnboardingRequest request, ClaimsPrincipal user, CurrentProfileService current,
        OnboardingWriteService service, CancellationToken ct)
    {
        if (OnboardingWriteService.Validate(request) is { } error)
            return ApiProblems.WithCode(400, "Invalid onboarding", error.Detail, error.Code);
        var profile = await current.ResolveAsync(user, ct);
        var (result, code) = await service.CompleteAsync(profile.Id, request, ct);
        return code is null ? TypedResults.Ok(result!) : Problem(code);
    }

    private static async Task<Ok<List<LegacyTankDto>>> LegacyAsync(
        ClaimsPrincipal user, CurrentProfileService current, CichlidsDbContext context, CancellationToken ct)
    {
        var profile = await current.ResolveAsync(user, ct);
        var tanks = await context.Tanks.Where(t => t.ProfileId == profile.Id && t.LegacyId != null && t.State == TankState.Published && t.DeletedAt == null)
            .OrderBy(t => t.Id).ToListAsync(ct);
        return TypedResults.Ok(tanks.Select(t => new LegacyTankDto(t.Id, t.Title, t.Region ?? TankRegions.FromLegacyCategory(t.Category))).ToList());
    }

    private static async Task<Results<Ok<string[]>, ProblemHttpResult>> InterestsAsync(
        InterestsRequest request, ClaimsPrincipal user, CurrentProfileService current, OnboardingWriteService service, CancellationToken ct)
    {
        if (!OnboardingWriteService.ValidInterests(request.Interests)) return Problem("interests_invalid");
        var profile = await current.ResolveAsync(user, ct);
        return TypedResults.Ok(await service.SetInterestsAsync(profile.Id, request.Interests, ct));
    }

    private static async Task<Results<Ok, ProblemHttpResult>> RefineAsync(
        long id, RefineRegionRequest request, ClaimsPrincipal user, CurrentProfileService current, OnboardingWriteService service, CancellationToken ct)
    {
        var profile = await current.ResolveAsync(user, ct);
        var code = await service.RefineAsync(profile.Id, id, request.Region, ct);
        return code is null ? TypedResults.Ok() : Problem(code);
    }

    private static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code == "legacy_tank_not_found" ? 404 : 400, "Onboarding could not be saved", "Check the selected tank and regions.", code);
}
