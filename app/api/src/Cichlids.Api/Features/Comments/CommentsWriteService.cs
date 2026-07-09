using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// Write logic shared by the picture and tank comment endpoints. A request splits into a comment
/// row and a rating row exactly like the legacy migration split them, the target post's
/// denormalized rating aggregate is recomputed from the rating base table (derived aggregates are
/// recomputed, never authored), and every write records its outbox event in the same transaction
/// so a dispatcher can deliver it without ever seeing a half-committed change.
/// </summary>
public sealed class CommentsWriteService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<CommentCreatedDto> CreateAsync(
        long? postId, long? tankId, Profile author, string? body, short? stars, CancellationToken cancellationToken)
    {
        var trimmedBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        Comment? comment = null;
        if (trimmedBody is not null)
        {
            comment = new Comment
            {
                PostId = postId,
                TankId = tankId,
                AuthorProfileId = author.Id,
                Body = trimmedBody,
                CreatedAt = now,
            };
            context.Comments.Add(comment);
        }

        Rating? rating = null;
        if (stars is { } starsValue)
        {
            rating = new Rating
            {
                PostId = postId,
                TankId = tankId,
                ProfileId = author.Id,
                Stars = starsValue,
                CreatedAt = now,
            };
            context.Ratings.Add(rating);
        }

        await context.SaveChangesAsync(cancellationToken);

        // Tanks have no denormalized rating aggregate columns, so only a post target is updated.
        if (rating is not null && postId is { } ratedPostId)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE post SET rating_count = agg.cnt, rating_average = agg.avg_stars
                FROM (
                    SELECT COUNT(*) AS cnt, AVG(stars)::float8 AS avg_stars
                    FROM rating WHERE post_id = {ratedPostId}
                ) AS agg
                WHERE post.id = {ratedPostId}
                """,
                cancellationToken);
        }

        var aggregateType = postId is not null ? "post" : "tank";
        var aggregateId = (postId ?? tankId)!.Value.ToString();

        if (comment is not null)
        {
            AddOutboxEvent("comment.created", aggregateType, aggregateId, now, new
            {
                commentId = comment.Id,
                postId,
                tankId,
                authorProfileId = author.Id,
            });
        }

        if (rating is not null)
        {
            AddOutboxEvent("rating.created", aggregateType, aggregateId, now, new
            {
                ratingId = rating.Id,
                postId,
                tankId,
                profileId = author.Id,
                stars = rating.Stars,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CommentCreatedDto(
            comment is not null ? await ToCommentDtoAsync(comment, cancellationToken) : null,
            rating is not null ? new CreatedRatingDto(rating.Id, rating.Stars) : null);
    }

    /// <summary>
    /// Marks a comment deleted with its moderation trail (moment, reason, acting moderator).
    /// Returns false when no visible comment with this id exists; a comment already deleted
    /// presents the same way, so a repeated delete cannot overwrite an existing trail.
    /// </summary>
    public async Task<bool> DeleteAsync(
        long commentId, long moderatorProfileId, string reason, CancellationToken cancellationToken)
    {
        var comment = await context.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.DeletedAt == null, cancellationToken);

        if (comment is null)
        {
            return false;
        }

        comment.DeletedAt = DateTimeOffset.UtcNow;
        comment.DeleteReason = reason;
        comment.DeletedByProfileId = moderatorProfileId;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void AddOutboxEvent(
        string eventType, string aggregateType, string aggregateId, DateTimeOffset occurredAt, object payload)
    {
        context.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = JsonSerializer.Serialize(payload, PayloadOptions),
        });
    }

    private async Task<CommentDto> ToCommentDtoAsync(Comment comment, CancellationToken cancellationToken)
    {
        var authors = await AuthorBatchLoader.LoadAsync(context, [comment.AuthorProfileId!.Value], cancellationToken);
        return new CommentDto(
            comment.Id,
            comment.Body,
            comment.CreatedAt,
            comment.Score,
            authors[comment.AuthorProfileId.Value].ToDto(objectStore),
            PosterName: null);
    }
}
