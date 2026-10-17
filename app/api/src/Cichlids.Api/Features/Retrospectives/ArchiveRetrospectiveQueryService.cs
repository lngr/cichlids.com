using Cichlids.Api.Features.Media;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Retrospectives;

/// <summary>
/// Builds the archive retrospective of a member from the migrated content of the member: photos,
/// the migrated comments and ratings other members left on the member's stories and tanks, the story
/// with the most views and the archive tanks that are not adopted yet. Comments and ratings written in
/// the app belong to the present, not to the archive, and do not count. A member without such content has no
/// retrospective. Images are links the member may use, signed where the object is not public.
/// </summary>
public sealed class ArchiveRetrospectiveQueryService(CichlidsDbContext context, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    private IQueryable<MediaItem> Photos(long profileId) =>
        context.MediaItems.Where(m => m.OwnerProfileId == profileId && m.LegacyId != null && m.Kind == MediaKind.Photo
            && !context.MediaLinks.Any(l => l.MediaItemId == m.Id && l.ContextType == MediaLinkContext.Profile));

    private IQueryable<Tank> OpenArchiveTanks(long profileId) =>
        context.Tanks.Where(t => t.ProfileId == profileId && t.LegacyId != null && t.AdoptedAt == null
            && t.State == TankState.Published && t.DeletedAt == null);

    private IQueryable<Story> ViewedStories(long profileId) =>
        context.Stories.Where(s => s.AuthorProfileId == profileId && s.LegacyId != null && s.State != StoryState.Draft
            && s.DeletedAt == null && s.ViewCount > 0);

    private IQueryable<Comment> CommentsReceived(long profileId) =>
        context.Comments.Where(c => c.LegacyId != null && c.DeletedAt == null && (c.AuthorProfileId == null || c.AuthorProfileId != profileId)
            && ((c.StoryId != null && context.Stories.Any(s => s.Id == c.StoryId && s.AuthorProfileId == profileId && s.DeletedAt == null))
                || (c.TankId != null && context.Tanks.Any(t => t.Id == c.TankId && t.ProfileId == profileId && t.DeletedAt == null))));

    private IQueryable<Rating> RatingsReceived(long profileId) =>
        context.Ratings.Where(r => r.LegacyCommentId != null && (r.ProfileId == null || r.ProfileId != profileId))
            .Where(r => (r.StoryId != null && context.Stories.Any(s => s.Id == r.StoryId && s.AuthorProfileId == profileId && s.DeletedAt == null))
                || (r.TankId != null && context.Tanks.Any(t => t.Id == r.TankId && t.ProfileId == profileId && t.DeletedAt == null)));

    /// <summary>
    /// Whether the member has a retrospective, which is the case when at least one card other than the
    /// member since card has data.
    /// </summary>
    public async Task<bool> ExistsAsync(long profileId, CancellationToken cancellationToken) =>
        await Photos(profileId).AnyAsync(cancellationToken)
        || await OpenArchiveTanks(profileId).AnyAsync(cancellationToken)
        || await ViewedStories(profileId).AnyAsync(cancellationToken)
        || await CommentsReceived(profileId).AnyAsync(cancellationToken)
        || await RatingsReceived(profileId).AnyAsync(cancellationToken);

    /// <summary>
    /// The retrospective of the member, or null when the member has none.
    /// </summary>
    public async Task<ArchiveRetrospectiveDto?> GetAsync(Profile profile, CancellationToken cancellationToken)
    {
        var photos = await Photos(profile.Id).CountAsync(cancellationToken);
        var comments = await CommentsReceived(profile.Id).CountAsync(cancellationToken);
        var stars = await RatingsReceived(profile.Id).SumAsync(r => (int)r.Stars, cancellationToken);
        var stats = photos + comments + stars > 0 ? new ArchiveStatsDto(photos, comments, stars) : null;

        var top = await ViewedStories(profile.Id).OrderByDescending(s => s.ViewCount).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.Title, s.ViewCount, s.CoverMediaLinkId }).FirstOrDefaultAsync(cancellationToken);
        var tanks = await OpenArchiveTanks(profile.Id).OrderBy(t => t.Id).Select(t => new { t.Id, t.Title }).ToListAsync(cancellationToken);
        if (stats is null && top is null && tanks.Count == 0)
        {
            return null;
        }

        var slug = top is null ? null : await context.SlugAliases
            .Where(a => a.StoryId == top.Id && a.IsCanonical).Select(a => a.Value).FirstOrDefaultAsync(cancellationToken);
        var coverItem = top is null ? null : await CoverMediaItemIdAsync(top.Id, top.CoverMediaLinkId, cancellationToken);
        var tankItems = await TankImageItemsAsync(tanks.Select(t => t.Id).ToList(), cancellationToken);

        var itemIds = tankItems.Values.ToList();
        if (coverItem is { } coverId)
        {
            itemIds.Add(coverId);
        }

        var images = await ImagesAsync(itemIds, profile.Id, cancellationToken);

        return new ArchiveRetrospectiveDto(
            LegacyProfiles.HasKnownCreatedAt(profile.CreatedAt)
                ? new ArchiveMemberSinceDto(profile.CreatedAt.UtcDateTime.Year, profile.CreatedAt.UtcDateTime.Month)
                : null,
            stats,
            top is null ? null : new ArchiveMostViewedDto(
                top.Id, slug ?? string.Empty, top.Title, top.ViewCount,
                coverItem is { } cover ? images.GetValueOrDefault(cover) : null),
            tanks.Select(t => new ArchiveTankDto(t.Id, t.Title, tankItems.TryGetValue(t.Id, out var item) ? images.GetValueOrDefault(item) : null)).ToList());
    }

    private async Task<long?> CoverMediaItemIdAsync(long storyId, long? coverLinkId, CancellationToken cancellationToken)
    {
        var links = await context.MediaLinks.StoryLinks().Where(l => l.StoryId == storyId)
            .OrderBy(l => l.Position).ThenBy(l => l.Id).Select(l => new { l.Id, l.MediaItemId }).ToListAsync(cancellationToken);
        return links.Count == 0 ? null : (links.FirstOrDefault(l => l.Id == coverLinkId) ?? links[0]).MediaItemId;
    }

    /// <summary>
    /// The image of each tank: the main image, otherwise the first showcase photo.
    /// </summary>
    private async Task<Dictionary<long, long>> TankImageItemsAsync(IReadOnlyCollection<long> tankIds, CancellationToken cancellationToken)
    {
        if (tankIds.Count == 0)
        {
            return [];
        }

        var rows = await context.MediaLinks.AsNoTracking()
            .Where(l => l.TankId != null && tankIds.Contains(l.TankId.Value) && (l.Role == MediaLinkRole.Main || l.Role == MediaLinkRole.Showcase))
            .Select(l => new { TankId = l.TankId!.Value, l.MediaItemId, l.Role, l.Position })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TankId).ToDictionary(g => g.Key,
            g => g.OrderBy(r => r.Role == MediaLinkRole.Main ? 0 : 1).ThenBy(r => r.Position).First().MediaItemId);
    }

    private async Task<Dictionary<long, ImageUrlsDto>> ImagesAsync(IReadOnlyCollection<long> mediaItemIds, long viewerProfileId, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var variants = (await context.MediaVariants.AsNoTracking().Where(v => mediaItemIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken))
            .ToLookup(v => v.MediaItemId);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(mediaItemIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(mediaItemIds, viewerProfileId, now, cancellationToken);
        return mediaItemIds.ToDictionary(id => id, id => _mediaUrlBuilder.Build(
            variants[id], allowSigned: true, anonymousVisible.Contains(id), generations.TryGetValue(id, out var generation) ? generation : null));
    }
}
