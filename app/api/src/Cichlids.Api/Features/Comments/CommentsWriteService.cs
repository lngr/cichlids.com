using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// Write logic shared by the picture and tank comment endpoints. A request splits into a comment
/// row and a rating row exactly like the legacy migration split them, the target story's
/// denormalized rating aggregate is recomputed from the rating base table (derived aggregates are
/// recomputed, never authored), and every write records its outbox event in the same transaction
/// so a dispatcher can deliver it without ever seeing a half-committed change. A member has one
/// rating per target: a repeat rating replaces stars and timestamp of the existing row in one
/// atomic upsert, and a member cannot rate own content.
/// </summary>
public sealed class CommentsWriteService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes the comment and the rating of one request. Returns null and writes nothing when the
    /// request carries stars for content owned by the author.
    /// </summary>
    public async Task<CommentCreatedDto?> CreateAsync(
        long? storyId, long? tankId, Profile author, string? body, short? stars, CancellationToken cancellationToken)
    {
        if (stars is not null && await IsOwnTargetAsync(storyId, tankId, author.Id, cancellationToken))
        {
            return null;
        }

        var trimmedBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        // Comment and rating share this timestamp; the comment list pairs a rating with its comment by it.
        // A replaced rating takes the timestamp of the request that replaced it, so its stars appear
        // at the newest comment of the member and the earlier comments show none.
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        Comment? comment = null;
        if (trimmedBody is not null)
        {
            comment = new Comment
            {
                StoryId = storyId,
                TankId = tankId,
                AuthorProfileId = author.Id,
                Body = trimmedBody,
                CreatedAt = now,
            };
            context.Comments.Add(comment);
        }

        await context.SaveChangesAsync(cancellationToken);

        UpsertedRating? rating = null;
        if (stars is { } starsValue)
        {
            rating = await UpsertRatingAsync(storyId, tankId, author.Id, starsValue, now, cancellationToken);

            // Tanks have no denormalized rating aggregate columns, so only a story target is updated.
            if (storyId is { } ratedStoryId)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE story SET rating_count = agg.cnt, rating_average = agg.avg_stars
                    FROM (
                        SELECT COUNT(*) AS cnt, AVG(stars)::float8 AS avg_stars
                        FROM rating WHERE story_id = {ratedStoryId}
                    ) AS agg
                    WHERE story.id = {ratedStoryId}
                    """,
                    cancellationToken);
            }
        }

        var aggregateType = storyId is not null ? "story" : "tank";
        var aggregateId = (storyId ?? tankId)!.Value.ToString();

        if (comment is not null)
        {
            AddOutboxEvent("comment.created", aggregateType, aggregateId, now, new
            {
                commentId = comment.Id,
                storyId,
                tankId,
                authorProfileId = author.Id,
            });
        }

        if (rating is not null)
        {
            if (rating.Inserted)
            {
                AddOutboxEvent("rating.created", aggregateType, aggregateId, now, new
                {
                    ratingId = rating.Id,
                    storyId,
                    tankId,
                    profileId = author.Id,
                    stars = rating.Stars,
                });
            }
            else
            {
                AddOutboxEvent("rating.updated", aggregateType, aggregateId, now, new
                {
                    ratingId = rating.Id,
                    storyId,
                    tankId,
                    profileId = author.Id,
                    stars = rating.Stars,
                    previousStars = rating.PreviousStars,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CommentCreatedDto(
            comment is not null ? await ToCommentDtoAsync(comment, rating?.Stars, cancellationToken) : null,
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

    private async Task<bool> IsOwnTargetAsync(
        long? storyId, long? tankId, long profileId, CancellationToken cancellationToken) =>
        storyId is { } ownedStoryId
            ? await context.Stories.AnyAsync(p => p.Id == ownedStoryId && p.AuthorProfileId == profileId, cancellationToken)
            : await context.Tanks.AnyAsync(t => t.Id == tankId && t.ProfileId == profileId, cancellationToken);

    /// <summary>
    /// Inserts the rating of the member or replaces the existing one in a single statement on the
    /// partial unique index of the target, so parallel requests never create two rows. A
    /// replacement takes the new stars and timestamp and is detached from any migrated comment, so the
    /// stars pair with the comment of the request by the shared timestamp.
    /// The previous stars come from the statement snapshot taken before the replacement.
    /// </summary>
    private async Task<UpsertedRating> UpsertRatingAsync(
        long? storyId, long? tankId, long profileId, short stars, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var rows = storyId is { } ratedStoryId
            ? await context.Database.SqlQuery<UpsertedRating>(
                $"""
                INSERT INTO rating (story_id, profile_id, stars, created_at)
                VALUES ({ratedStoryId}, {profileId}, {stars}, {createdAt})
                ON CONFLICT (profile_id, story_id) WHERE profile_id IS NOT NULL AND story_id IS NOT NULL
                DO UPDATE SET stars = EXCLUDED.stars, created_at = EXCLUDED.created_at, legacy_comment_id = NULL
                RETURNING id, stars, (xmax = 0) AS inserted,
                    (SELECT previous.stars FROM rating previous
                     WHERE previous.profile_id = {profileId} AND previous.story_id = {ratedStoryId}) AS previous_stars
                """).ToListAsync(cancellationToken)
            : await context.Database.SqlQuery<UpsertedRating>(
                $"""
                INSERT INTO rating (tank_id, profile_id, stars, created_at)
                VALUES ({tankId}, {profileId}, {stars}, {createdAt})
                ON CONFLICT (profile_id, tank_id) WHERE profile_id IS NOT NULL AND tank_id IS NOT NULL
                DO UPDATE SET stars = EXCLUDED.stars, created_at = EXCLUDED.created_at, legacy_comment_id = NULL
                RETURNING id, stars, (xmax = 0) AS inserted,
                    (SELECT previous.stars FROM rating previous
                     WHERE previous.profile_id = {profileId} AND previous.tank_id = {tankId}) AS previous_stars
                """).ToListAsync(cancellationToken);

        return rows.Single();
    }

    private sealed record UpsertedRating(long Id, short Stars, bool Inserted, short? PreviousStars);

    private void AddOutboxEvent(
        string eventType, string aggregateType, string aggregateId, DateTimeOffset occurredAt, object payload)
    {
        context.OutboxEvents.Add(OutboxEvent.Create(
            eventType, aggregateType, aggregateId, JsonSerializer.Serialize(payload, PayloadOptions), occurredAt));
    }

    private async Task<CommentDto> ToCommentDtoAsync(Comment comment, short? stars, CancellationToken cancellationToken)
    {
        var authors = await AuthorBatchLoader.LoadAsync(context, [comment.AuthorProfileId!.Value], cancellationToken);
        return new CommentDto(
            comment.Id,
            comment.Body,
            comment.CreatedAt,
            comment.Score,
            stars,
            authors[comment.AuthorProfileId.Value].ToDto(objectStore),
            PosterName: null);
    }
}
