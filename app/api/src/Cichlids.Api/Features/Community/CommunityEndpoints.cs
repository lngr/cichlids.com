using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Community;

public static class CommunityEndpoints
{
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/community").WithTags("Community");

        group.MapGet("/categories", ListCategoriesAsync).WithName("ListCommunityCategories");
        group.MapGet("/threads", ListThreadsAsync).WithName("ListCommunityThreads");
        group.MapGet("/threads/{id:long}", GetThreadAsync).WithName("GetCommunityThread");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<CommunityCategoryDto>>> ListCategoriesAsync(
        CommunityQueryService queryService, CancellationToken cancellationToken)
    {
        var result = await queryService.GetCategoriesAsync(cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<PagedResponse<CommunityThreadListItemDto>>, BadRequest<string>>> ListThreadsAsync(
        CommunityQueryService queryService,
        string? category,
        string? query,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        DiscussionCategory? categoryFilter = null;
        if (category is not null)
        {
            if (!SnakeCaseEnum.TryParse<DiscussionCategory>(category, out var parsedCategory))
            {
                return TypedResults.BadRequest($"Unknown category '{category}'.");
            }

            categoryFilter = parsedCategory;
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListThreadsAsync(categoryFilter, query, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CommunityThreadDetailDto>, NotFound>> GetThreadAsync(
        CommunityQueryService queryService, long id, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetThreadDetailAsync(id, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }
}
