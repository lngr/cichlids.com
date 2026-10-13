using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
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
            .RequireAuthorization(AuthorizationPolicies.Moderator)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public const int MaxBodyLength = 10000;
    public const int MaxReasonLength = 500;
    public const string CommentTooLongCode = "comment_too_long";
    public const string ReasonTooLongCode = "reason_too_long";
    public const string SelfRatingCode = "self_rating";

    /// <summary>
    /// The 400 problem details answering a comment body above the maximum length. The stable
    /// code comment_too_long lets a client tell it from other validation errors.
    /// </summary>
    public static ProblemHttpResult CommentTooLongProblem() =>
        ApiProblems.WithCode(
            StatusCodes.Status400BadRequest,
            "Comment too long",
            $"A comment must be at most {MaxBodyLength} characters.",
            CommentTooLongCode);

    /// <summary>
    /// The 400 problem details answering a deletion reason above the maximum length, with the
    /// code reason_too_long.
    /// </summary>
    public static ProblemHttpResult ReasonTooLongProblem() =>
        ApiProblems.WithCode(
            StatusCodes.Status400BadRequest,
            "Reason too long",
            $"A delete reason must be at most {MaxReasonLength} characters.",
            ReasonTooLongCode);

    /// <summary>
    /// The 403 problem details answering stars on content owned by the author. The stable code
    /// self_rating lets a client tell it from other refusals.
    /// </summary>
    public static ProblemHttpResult SelfRatingProblem() =>
        ApiProblems.WithCode(
            StatusCodes.Status403Forbidden,
            "Own content cannot be rated",
            "Members cannot rate their own pictures and tanks.",
            SelfRatingCode);

    /// <summary>
    /// Whether a trimmed comment body is longer than MaxBodyLength characters.
    /// </summary>
    public static bool IsBodyTooLong(string? body) => body is { Length: > MaxBodyLength };

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

    private static async Task<Results<NoContent, BadRequest<string>, NotFound, ProblemHttpResult>> DeleteAsync(
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

        if (reason.Length > MaxReasonLength)
        {
            return ReasonTooLongProblem();
        }

        var moderator = await currentProfileService.ResolveAsync(user, cancellationToken);

        var deleted = await commentsWriteService.DeleteAsync(id, moderator.Id, reason, cancellationToken);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
