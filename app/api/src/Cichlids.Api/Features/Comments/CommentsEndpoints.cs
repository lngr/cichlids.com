using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Comments;

public static class CommentsEndpoints
{
    public static IEndpointRouteBuilder MapCommentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/comments/{id:long}", DeleteAsync)
            .WithName("DeleteComment")
            .WithTags("Comments")
            .RequireAuthorization(AuthorizationPolicies.Moderator);

        return app;
    }

    /// <summary>
    /// Validates a comment post request: a non-empty body, stars between 1 and 5, and at least
    /// one of the two. Returns the trimmed body and narrowed stars for the write path, or an
    /// error message for the 400 response.
    /// </summary>
    public static bool TryValidateCreate(
        CreateCommentRequest request, out string? body, out short? stars, out string error)
    {
        body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim();
        stars = null;
        error = string.Empty;

        if (request.Stars is { } starsValue)
        {
            if (starsValue is < 1 or > 5)
            {
                error = "stars must be between 1 and 5.";
                return false;
            }

            stars = (short)starsValue;
        }

        if (body is null && stars is null)
        {
            error = "Provide a non-empty body, stars, or both.";
            return false;
        }

        return true;
    }

    private static async Task<Results<NoContent, BadRequest<string>, NotFound>> DeleteAsync(
        CommentsWriteService commentsWriteService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        long id,
        [FromBody] DeleteCommentRequest request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return TypedResults.BadRequest("A delete reason is required.");
        }

        var moderator = await currentProfileService.ResolveAsync(user, cancellationToken);

        var deleted = await commentsWriteService.DeleteAsync(id, moderator.Id, reason, cancellationToken);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
