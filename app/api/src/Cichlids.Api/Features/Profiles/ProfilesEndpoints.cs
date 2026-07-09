using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Tanks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Profiles;

public static class ProfilesEndpoints
{
    public static IEndpointRouteBuilder MapProfilesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profiles").WithTags("Profiles");

        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetProfile");
        group.MapGet("/{id:long}/pictures", ListPicturesAsync).WithName("ListProfilePictures");
        group.MapGet("/{id:long}/tanks", ListTanksAsync).WithName("ListProfileTanks");

        return app;
    }

    private static async Task<Results<Ok<ProfileDetailDto>, NotFound>> GetByIdAsync(
        ProfilesQueryService queryService, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Ok<PagedResponse<PictureListItemDto>>> ListPicturesAsync(
        PicturesQueryService queryService, long id, string? sort, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(sort, topic: null, userId: id, species: null, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<PagedResponse<TankListItemDto>>> ListTanksAsync(
        TanksQueryService queryService, long id, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(userId: id, category: null, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }
}
