using Cichlids.Api.Features.Media;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Lists a profile's draft stories, newest first, each with the image URLs of its first photo: signed paths
/// for photos that only the author sees and public URLs for photos that anonymous viewers see anyway.
/// </summary>
public sealed class DraftsQueryService(CichlidsDbContext context, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    private static readonly IReadOnlyList<MediaVariant> NoVariants = [];
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    public async Task<IReadOnlyList<DraftDto>> ListAsync(long authorProfileId, CancellationToken cancellationToken, int limit = int.MaxValue)
    {
        var drafts = await context.Stories
            .Where(p => p.AuthorProfileId == authorProfileId && p.State == StoryState.Draft && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .Select(p => new { p.Id, p.State, p.Topic, p.Title, p.Description, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var storyIds = drafts.Select(d => d.Id).ToList();

        var mediaItemIdByStory = (await context.MediaLinks.StoryLinks()
                .Where(l => storyIds.Contains(l.StoryId!.Value))
                .Select(l => new { StoryId = l.StoryId!.Value, l.MediaItemId, Sort = l.Position })
                .ToListAsync(cancellationToken))
            .GroupBy(pm => pm.StoryId)
            .ToDictionary(g => g.Key, g => g.OrderBy(pm => pm.Sort).First().MediaItemId);

        var mediaItemIds = mediaItemIdByStory.Values.ToList();

        var variantsByMediaItem = (await context.MediaVariants.AsNoTracking()
                .Where(v => mediaItemIds.Contains(v.MediaItemId))
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MediaVariant>)g.ToList());
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(mediaItemIds, DateTimeOffset.UtcNow, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(mediaItemIds, authorProfileId, DateTimeOffset.UtcNow, cancellationToken);

        // Every draft the upload endpoint creates has exactly one photo; a draft without one has
        // nothing to show and is left out.
        return drafts
            .Where(d => mediaItemIdByStory.ContainsKey(d.Id))
            .Select(d =>
            {
                var mediaItemId = mediaItemIdByStory[d.Id];
                var image = _mediaUrlBuilder.Build(variantsByMediaItem.GetValueOrDefault(mediaItemId, NoVariants), allowSigned: true, anonymousVisible.Contains(mediaItemId), generations.TryGetValue(mediaItemId, out var generation) ? generation : null);
                return new DraftDto(d.Id, d.State, d.Topic, d.Title, d.Description, d.CreatedAt, image);
            })
            .ToList();
    }
}
