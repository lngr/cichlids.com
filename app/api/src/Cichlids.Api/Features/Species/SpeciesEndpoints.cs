using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Species;

public static class SpeciesEndpoints
{
    public static IEndpointRouteBuilder MapSpeciesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/species").WithTags("Species");

        group.MapGet("/", ListAsync).WithName("ListSpecies");
        group.MapGet("/{idOrSlug}", GetByIdOrSlugAsync).WithName("GetSpecies");

        return app;
    }

    private static async Task<Ok<PagedResponse<SpeciesListItemDto>>> ListAsync(
        SpeciesQueryService queryService, string? query, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(query, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<SpeciesDetailDto>, NotFound>> GetByIdOrSlugAsync(
        SpeciesQueryService queryService, string idOrSlug, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdOrSlugAsync(idOrSlug, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }
}
