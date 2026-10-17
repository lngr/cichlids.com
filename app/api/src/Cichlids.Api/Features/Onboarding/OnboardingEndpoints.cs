using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Library;
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
        app.MapGet("/api/me/legacy-tanks/{id:long}/photo-suggestions", SuggestionsAsync).WithName("ListLegacyTankPhotoSuggestions").WithTags("Me").RequireRegistered()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        app.MapPost("/api/me/legacy-tanks/{id:long}/adopt", AdoptAsync).WithName("AdoptLegacyTank").WithTags("Me").RequireRegistered()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
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
        var tanks = await context.Tanks.Where(t => t.ProfileId == profile.Id && t.LegacyId != null && t.AdoptedAt == null
                && t.State == TankState.Published && t.DeletedAt == null)
            .OrderBy(t => t.Id).ToListAsync(ct);
        return TypedResults.Ok(tanks.Select(t => new LegacyTankDto(t.Id, t.Title, t.Region ?? TankRegions.FromLegacyCategory(t.Category))).ToList());
    }

    private static async Task<Results<Ok<PagedResponse<PhotoSuggestionDto>>, ProblemHttpResult>> SuggestionsAsync(
        long id, int? offset, int? limit, ClaimsPrincipal user, CurrentProfileService current,
        ArchiveTankAdoptionService service, CancellationToken ct)
    {
        var profile = await current.ResolveAsync(user, ct);
        var (skip, take) = Pagination.Normalize(offset, limit);
        var (page, error) = await service.SuggestAsync(profile.Id, id, skip, take, ct);
        return error is null ? TypedResults.Ok(page!) : AdoptionProblem(error);
    }

    private static async Task<Results<Ok<AdoptTankResultDto>, ProblemHttpResult>> AdoptAsync(
        long id, AdoptTankRequest request, ClaimsPrincipal user, CurrentProfileService current,
        ArchiveTankAdoptionService service, CancellationToken ct)
    {
        if (!ArchiveTankAdoptionService.ValidPhotoIds(request.MediaItemIds))
            return AdoptionProblem(ArchiveTankAdoptionService.PhotosInvalidCode);
        var profile = await current.ResolveAsync(user, ct);
        var (result, error) = await service.AdoptAsync(profile.Id, id, request.MediaItemIds, ct);
        return error is null ? TypedResults.Ok(result!) : AdoptionProblem(error);
    }

    private static ProblemHttpResult AdoptionProblem(string code) => code switch
    {
        ArchiveTankAdoptionService.TankNotFoundCode => ApiProblems.WithCode(404, "Archive tank not found", "Choose one of your archive tanks.", code),
        ArchiveTankAdoptionService.AlreadyAdoptedCode => ApiProblems.WithCode(409, "Tank already adopted", "The tank is already adopted.", code),
        MediaLibraryService.NotFoundCode => ApiProblems.WithCode(404, "Photo not found", "Choose photos from your own library.", code),
        _ => ApiProblems.WithCode(400, "Photos cannot be assigned", "Choose each photo once and at most 100 photos.", code),
    };

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
