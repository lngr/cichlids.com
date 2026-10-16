using System.Security.Claims;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Stories;
using Cichlids.Api.Features.Tanks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Profiles;

public static class ProfilesEndpoints
{
    public static IEndpointRouteBuilder MapProfilesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profiles").WithTags("Profiles");

        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetProfile");
        group.MapGet("/{id:long}/stories", ListStoriesAsync).WithName("ListProfileStories");
        group.MapGet("/{id:long}/tanks", ListTanksAsync).WithName("ListProfileTanks");

        return app;
    }

    private static async Task<Results<Ok<ProfileDetailDto>, NotFound>> GetByIdAsync(
        ProfilesQueryService queryService, TankAccessService access, ClaimsPrincipal viewer, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, await access.ViewerProfileIdAsync(viewer, cancellationToken), cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<PagedResponse<StoryListItemDto>>, NotFound>> ListStoriesAsync(
        StoriesQueryService queryService, ProfilesQueryService profiles, TankAccessService access, ClaimsPrincipal viewer, long id,
        string? sort, int? offset, int? limit, CancellationToken cancellationToken)
    {
        if (await profiles.IsForeignProvisionalAsync(id, await access.ViewerProfileIdAsync(viewer, cancellationToken), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(sort, topic: null, userId: id, species: null, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<PagedResponse<TankListItemDto>>, NotFound>> ListTanksAsync(
        TanksQueryService queryService, ProfilesQueryService profiles, TankAccessService access, ClaimsPrincipal viewer, long id,
        int? offset, int? limit, CancellationToken cancellationToken)
    {
        var viewerProfileId = await access.ViewerProfileIdAsync(viewer, cancellationToken);
        if (await profiles.IsForeignProvisionalAsync(id, viewerProfileId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(
            userId: id, category: null, viewerProfileId, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }
}
