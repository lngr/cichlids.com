using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Lists a profile's draft posts, newest first, each with the image URLs of its first photo.
/// </summary>
public sealed class DraftsQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariants = new Dictionary<string, string>();
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    public async Task<IReadOnlyList<DraftDto>> ListAsync(long authorProfileId, CancellationToken cancellationToken)
    {
        var drafts = await context.Posts
            .Where(p => p.AuthorProfileId == authorProfileId && p.State == PostState.Draft && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new { p.Id, p.State, p.Topic, p.Title, p.Description, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var postIds = drafts.Select(d => d.Id).ToList();

        var mediaItemIdByPost = (await context.PostMedia
                .Where(pm => postIds.Contains(pm.PostId))
                .Select(pm => new { pm.PostId, pm.MediaItemId, pm.Sort })
                .ToListAsync(cancellationToken))
            .GroupBy(pm => pm.PostId)
            .ToDictionary(g => g.Key, g => g.OrderBy(pm => pm.Sort).First().MediaItemId);

        var mediaItemIds = mediaItemIdByPost.Values.ToList();

        var originalByMediaItem = await context.MediaItems
            .Where(m => mediaItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var variantsByMediaItem = (await context.MediaVariants
                .Where(v => mediaItemIds.Contains(v.MediaItemId))
                .Select(v => new { v.MediaItemId, v.Label, v.StorageKey })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(v => v.Label, v => v.StorageKey));

        // Every draft the upload endpoint creates has exactly one photo; a draft without one has
        // nothing to show and is left out.
        return drafts
            .Where(d => mediaItemIdByPost.ContainsKey(d.Id))
            .Select(d =>
            {
                var mediaItemId = mediaItemIdByPost[d.Id];
                var image = _mediaUrlBuilder.Build(
                    originalByMediaItem[mediaItemId], variantsByMediaItem.GetValueOrDefault(mediaItemId, NoVariants));
                return new DraftDto(d.Id, d.State, d.Topic, d.Title, d.Description, d.CreatedAt, image);
            })
            .ToList();
    }
}
