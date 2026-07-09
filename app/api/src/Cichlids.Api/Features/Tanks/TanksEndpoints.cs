using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Tanks;

public static class TanksEndpoints
{
    public static IEndpointRouteBuilder MapTanksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tanks").WithTags("Tanks");

        group.MapGet("/", ListAsync).WithName("ListTanks");
        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetTank");
        group.MapGet("/{id:long}/comments", ListCommentsAsync).WithName("ListTankComments");

        return app;
    }

    private static async Task<Results<Ok<PagedResponse<TankListItemDto>>, BadRequest<string>>> ListAsync(
        TanksQueryService queryService, long? user, string? category, int? offset, int? limit, CancellationToken cancellationToken)
    {
        TankCategory? categoryFilter = null;
        if (category is not null)
        {
            if (!SnakeCaseEnum.TryParse<TankCategory>(category, out var parsedCategory))
            {
                return TypedResults.BadRequest($"Unknown category '{category}'.");
            }

            categoryFilter = parsedCategory;
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(user, categoryFilter, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<TankDetailDto>, NotFound>> GetByIdAsync(
        TanksQueryService queryService, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetByIdAsync(id, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<PagedResponse<CommentDto>>, NotFound>> ListCommentsAsync(
        TanksQueryService tanksQueryService,
        CommentsQueryService commentsQueryService,
        long id,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await tanksQueryService.IsVisibleAsync(id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await commentsQueryService.ListForTankAsync(id, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }
}
