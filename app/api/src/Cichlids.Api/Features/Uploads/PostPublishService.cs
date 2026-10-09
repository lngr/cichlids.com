using System.Text.Json;
using Cichlids.Api.Features.Pictures;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Publishes drafts. The state change, the canonical slug and the post.published outbox event
/// are written in one transaction.
/// </summary>
public sealed class PostPublishService(
    CichlidsDbContext context,
    SlugGenerator slugGenerator,
    PicturesQueryService picturesQueryService)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Publishes the author's draft: sets the title, description and topic, the published state
    /// and moment, mints the canonical slug and records the post.published outbox event. Returns
    /// the published picture's detail, or the reason it was not published.
    /// </summary>
    public async Task<(DraftActionOutcome Outcome, PictureDetailDto? Detail)> PublishAsync(
        long postId, long authorProfileId, string title, string? description, PostTopic topic, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The state condition in the update itself makes two concurrent publishes of the same
        // draft race on the row lock, so exactly one of them sees the draft state.
        var updated = await context.Posts
            .Where(p => p.Id == postId && p.AuthorProfileId == authorProfileId && p.DeletedAt == null && p.State == PostState.Draft)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Title, title)
                    .SetProperty(p => p.Description, description)
                    .SetProperty(p => p.Topic, topic)
                    .SetProperty(p => p.State, PostState.Published)
                    .SetProperty(p => p.PublishedAt, now),
                cancellationToken);

        if (updated == 0)
        {
            var exists = await context.Posts
                .AnyAsync(p => p.Id == postId && p.AuthorProfileId == authorProfileId && p.DeletedAt == null, cancellationToken);
            return (exists ? DraftActionOutcome.NotDraft : DraftActionOutcome.NotFound, null);
        }

        var slug = slugGenerator.GenerateUnique(
            $"post-id:{postId}",
            candidate => context.SlugAliases.Any(s => s.Value == candidate));

        context.SlugAliases.Add(new SlugAlias { PostId = postId, Value = slug, IsCanonical = true, CreatedAt = now });
        context.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = now,
            EventType = "post.published",
            AggregateType = "post",
            AggregateId = postId.ToString(),
            Payload = JsonSerializer.Serialize(new { postId, slug, authorProfileId }, PayloadOptions),
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (DraftActionOutcome.Succeeded, await picturesQueryService.GetDetailAsync(postId, cancellationToken));
    }
}
