using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Comments;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Pictures;

public static class PicturesEndpoints
{
    private static readonly string[] ValidSorts = ["newest", "views", "rating"];

    public static IEndpointRouteBuilder MapPicturesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pictures").WithTags("Pictures");

        group.MapGet("/", ListAsync).WithName("ListPictures");
        group.MapGet("/{slug}", GetBySlugAsync).WithName("GetPicture");
        group.MapGet("/{slug}/comments", ListCommentsAsync).WithName("ListPictureComments");
        group.MapPost("/{slug}/comments", CreateCommentAsync).WithName("CreatePictureComment").RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<PagedResponse<PictureListItemDto>>, BadRequest<string>>> ListAsync(
        PicturesQueryService queryService,
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

        PostTopic? topicFilter = null;
        if (topic is not null)
        {
            if (!SnakeCaseEnum.TryParse<PostTopic>(topic, out var parsedTopic))
            {
                return TypedResults.BadRequest($"Unknown topic '{topic}'.");
            }

            topicFilter = parsedTopic;
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await queryService.ListAsync(sort, topicFilter, user, species, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<PictureDetailDto>, NotFound>> GetBySlugAsync(
        PicturesQueryService queryService, string slug, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetBySlugAsync(slug, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<PagedResponse<CommentDto>>, NotFound>> ListCommentsAsync(
        PicturesQueryService picturesQueryService,
        CommentsQueryService commentsQueryService,
        string slug,
        int? offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var postId = await picturesQueryService.ResolveVisiblePostIdAsync(slug, cancellationToken);
        if (postId is null)
        {
            return TypedResults.NotFound();
        }

        var (normalizedOffset, normalizedLimit) = Pagination.Normalize(offset, limit);
        var result = await commentsQueryService.ListForPostAsync(postId.Value, normalizedOffset, normalizedLimit, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<CommentCreatedDto>, BadRequest<string>, NotFound>> CreateCommentAsync(
        PicturesQueryService picturesQueryService,
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

        var postId = await picturesQueryService.ResolveVisiblePostIdAsync(slug, cancellationToken);
        if (postId is null)
        {
            return TypedResults.NotFound();
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var created = await commentsWriteService.CreateAsync(postId, tankId: null, author, body, stars, cancellationToken);
        return TypedResults.Created($"/api/pictures/{slug}/comments", created);
    }
}
