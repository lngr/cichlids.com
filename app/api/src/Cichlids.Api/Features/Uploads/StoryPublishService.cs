using System.Text.Json;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Stories;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Slugs;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Publishes drafts. The state change, the canonical slug, and the story.published outbox event are
/// written in one transaction; the state change marks the media items of the story for placement. A published story is
/// public, so its media items move into the public area; the publication waits for that within the
/// configured budget.
/// </summary>
public sealed class StoryPublishService(
    CichlidsDbContext context,
    SlugGenerator slugGenerator,
    StoriesQueryService storiesQueryService,
    MediaPlacementTrigger placement)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Publishes the author's draft: sets the title, description and topic, the published state
    /// and moment, mints the canonical slug and records the story.published outbox event. Returns
    /// the published picture's detail, or the reason it was not published.
    /// </summary>
    public async Task<(DraftActionOutcome Outcome, StoryDetailDto? Detail)> PublishAsync(
        long storyId, long authorProfileId, string title, string? description, StoryTopic topic, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The state condition in the update itself makes two concurrent publishes of the same
        // draft race on the row lock, so exactly one of them sees the draft state.
        var updated = await context.Stories
            .Where(p => p.Id == storyId && p.AuthorProfileId == authorProfileId && p.DeletedAt == null && p.State == StoryState.Draft)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Title, title)
                    .SetProperty(p => p.Description, description)
                    .SetProperty(p => p.Topic, topic)
                    .SetProperty(p => p.State, StoryState.Published)
                    .SetProperty(p => p.PublishedAt, now)
                    .SetProperty(p => p.UpdatedAt, now),
                cancellationToken);

        if (updated == 0)
        {
            var exists = await context.Stories
                .AnyAsync(p => p.Id == storyId && p.AuthorProfileId == authorProfileId && p.DeletedAt == null, cancellationToken);
            return (exists ? DraftActionOutcome.NotDraft : DraftActionOutcome.NotFound, null);
        }

        var slug = slugGenerator.GenerateUnique(
            $"story-id:{storyId}",
            candidate => context.SlugAliases.Any(s => s.Value == candidate));

        context.SlugAliases.Add(new SlugAlias { StoryId = storyId, Value = slug, IsCanonical = true, CreatedAt = now });
        context.OutboxEvents.Add(OutboxEvent.Create(
            "story.published",
            "story",
            storyId.ToString(),
            JsonSerializer.Serialize(new { storyId, slug, authorProfileId }, PayloadOptions),
            now));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await placement.PlaceNowAsync(await context.MediaItemIdsOfStory(storyId).ToListAsync(cancellationToken));
        return (DraftActionOutcome.Succeeded, await storiesQueryService.GetDetailAsync(storyId, authorProfileId, cancellationToken));
    }
}
