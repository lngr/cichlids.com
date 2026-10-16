using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Stories;

public static class StoriesEndpoints
{
    private static readonly string[] ValidSorts = ["newest", "views", "rating"];

    public static IEndpointRouteBuilder MapStoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stories").WithTags("Pictures");

        group.MapGet("/", ListAsync).WithName("ListStories");
        group.MapGet("/{slug}", GetBySlugAsync).WithName("GetStory");
        group.MapGet("/{slug}/comments", ListCommentsAsync).WithName("ListStoryComments");
        group.MapPost("/{slug}/comments", CreateCommentAsync).WithName("CreateStoryComment")
            .RequireRegistered()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<PagedResponse<StoryListItemDto>>, BadRequest<string>>> ListAsync(
        StoriesQueryService queryService,
        string? sort,
        string? topic,
        long? user,
        string? species,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (sort is not null && !ValidSorts.Contains(sort))
        {
            return TypedResults.BadRequest($"Unknown sort '{sort}'. Expected one of: {string.Join(", ", ValidSorts)}.");
        }

        StoryTopic? topicFilter = null;
        if (topic is not null)
        {
            if (!SnakeCaseEnum.TryParse<StoryTopic>(topic, out var parsedTopic))
            {
                return TypedResults.BadRequest($"Unknown topic '{topic}'.");
            }

            topicFilter = parsedTopic;
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(sort, topicFilter, user, species, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<StoryDetailDto>, NotFound>> GetBySlugAsync(
        StoriesQueryService queryService, TankAccessService access, ClaimsPrincipal viewer, string slug, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetBySlugAsync(slug, await access.ViewerProfileIdAsync(viewer, cancellationToken), cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<PagedResponse<CommentDto>>, NotFound>> ListCommentsAsync(
        StoriesQueryService storiesQueryService,
        CommentsQueryService commentsQueryService,
        string slug,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var storyId = await storiesQueryService.ResolveVisibleStoryIdAsync(slug, cancellationToken);
        if (storyId is null)
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await commentsQueryService.ListForStoryAsync(storyId.Value, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<CommentCreatedDto>, BadRequest<string>, NotFound, ProblemHttpResult>> CreateCommentAsync(
        StoriesQueryService storiesQueryService,
        CommentsWriteService commentsWriteService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        string slug,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (!CommentsEndpoints.TryValidateCreate(request, out var body, out var stars, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        if (CommentsEndpoints.IsBodyTooLong(body))
        {
            return CommentsEndpoints.CommentTooLongProblem();
        }

        var storyId = await storiesQueryService.ResolveVisibleStoryIdAsync(slug, cancellationToken);
        if (storyId is null)
        {
            return TypedResults.NotFound();
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var created = await commentsWriteService.CreateAsync(storyId, tankId: null, author, body, stars, cancellationToken);
        return created is null
            ? CommentsEndpoints.SelfRatingProblem()
            : TypedResults.Created($"/api/stories/{slug}/comments", created);
    }
}
