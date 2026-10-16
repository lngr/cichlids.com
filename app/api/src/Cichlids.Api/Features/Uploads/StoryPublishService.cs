using System.Text.Json;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Stories;
using Cichlids.Domain;
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
    /// and moment, mints the canonical slug (a story that was published before keeps its slug) and
    /// records the story.published outbox event. Returns the detail of the published story, or the
    /// reason it was not published: it does not exist, it is no draft or it has no item.
    /// </summary>
    public async Task<(DraftActionOutcome Outcome, StoryDetailDto? Detail)> PublishAsync(
        long storyId, long authorProfileId, string title, string? description, StoryTopic topic, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The row lock serializes the publication against edits of the items of the story.
        var state = await context.LockOwnStoryAsync(storyId, authorProfileId, cancellationToken);
        if (state is null)
        {
            return (DraftActionOutcome.NotFound, null);
        }

        if (state != StoryState.Draft)
        {
            return (DraftActionOutcome.NotDraft, null);
        }

        if (!await context.MediaLinks.StoryLinks().AnyAsync(l => l.StoryId == storyId, cancellationToken))
        {
            return (DraftActionOutcome.NoItems, null);
        }

        await context.Stories
            .Where(p => p.Id == storyId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Title, title)
                    .SetProperty(p => p.Description, description)
                    .SetProperty(p => p.Topic, topic)
                    .SetProperty(p => p.State, StoryState.Published)
                    .SetProperty(p => p.PublishedAt, now)
                    .SetProperty(p => p.UpdatedAt, now),
                cancellationToken);

        await RecordPublicationAsync(storyId, authorProfileId, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await placement.PlaceNowAsync(await context.MediaItemIdsOfStory(storyId).ToListAsync(cancellationToken));
        return (DraftActionOutcome.Succeeded, await storiesQueryService.GetDetailAsync(storyId, authorProfileId, cancellationToken));
    }

    /// <summary>
    /// Adds the records of a publication to the open transaction: the canonical slug, which a story that
    /// was published before keeps, and the story.published outbox event.
    /// </summary>
    public async Task RecordPublicationAsync(long storyId, long authorProfileId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var slug = await context.SlugAliases.Where(a => a.StoryId == storyId && a.IsCanonical).Select(a => a.Value).FirstOrDefaultAsync(cancellationToken);
        if (slug is null)
        {
            slug = slugGenerator.GenerateUnique(
                $"story-id:{storyId}",
                candidate => context.SlugAliases.Any(s => s.Value == candidate));
            context.SlugAliases.Add(new SlugAlias { StoryId = storyId, Value = slug, IsCanonical = true, CreatedAt = now });
        }

        context.OutboxEvents.Add(OutboxEvent.Create(
            "story.published",
            "story",
            storyId.ToString(),
            JsonSerializer.Serialize(new { storyId, slug, authorProfileId }, PayloadOptions),
            now));
    }
}
