using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Discover;

public static class DiscoverEndpoints
{
    public static IEndpointRouteBuilder MapDiscoverEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/discover", GetAsync).WithTags("Discover").WithName("GetDiscover")
            .AllowAnonymous().ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }

    private static async Task<Results<Ok<DiscoverDto>, ProblemHttpResult>> GetAsync(
        DiscoverQueryService service, string? regions, CancellationToken cancellationToken)
    {
        var selected = new List<TankRegion>();
        if (regions is not null)
        {
            foreach (var token in regions.Split(','))
            {
                if (!SnakeCaseEnum.TryParse<TankRegion>(token, out var region) || region == TankRegion.AmericasUnassigned)
                    return ApiProblems.WithCode(400, "Invalid regions", "Choose one or more selectable regions.", "discover_invalid_regions");
                selected.Add(region);
            }
        }
        return TypedResults.Ok(await service.GetAsync(regions is null ? DiscoverQueryService.SelectableRegions : selected, cancellationToken));
    }
}
