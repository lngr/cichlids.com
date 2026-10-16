using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Streaks;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Library;

/// <summary>
/// Reads the media library of a member: all files the member owns, newest first, with signed URLs and the
/// places that show each file. A place counts while its context is alive; the links of a deleted tank
/// or of a removed story do not make a file linked. The name of a place appears only when the place
/// belongs to the member.
/// </summary>
public sealed class MediaLibraryQueryService(
    CichlidsDbContext context, IObjectStore objectStore, TimeProvider clock, StreakService streaks, MediaUrlSigner? signer = null)
{
    private readonly MediaUrlBuilder _urls = new(objectStore, signer);

    /// <summary>
    /// A page of the files of the member. The kind and the linked state narrow the list; null keeps all.
    /// </summary>
    public async Task<PagedResponse<MediaLibraryItemDto>> ListAsync(
        long profileId, MediaKind? kind, bool? linked, int offset, int limit, CancellationToken cancellationToken)
    {
        var live = LiveLinks();
        var files = context.MediaItems.AsNoTracking().Where(m => m.OwnerProfileId == profileId);
        if (kind is { } wanted) files = files.Where(m => m.Kind == wanted);
        if (linked is true) files = files.Where(m => live.Any(l => l.MediaItemId == m.Id));
        if (linked is false) files = files.Where(m => !live.Any(l => l.MediaItemId == m.Id));

        var total = await files.CountAsync(cancellationToken);
        var page = await VideoQueryService.WithDetails(files)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new PagedResponse<MediaLibraryItemDto>(total, await ToDtosAsync(page, profileId, cancellationToken));
    }

    /// <summary>
    /// The file with its provenance, its places and the streak weeks it keeps on the given local date, or null when the member does not own a file with the id.
    /// </summary>
    public async Task<MediaLibraryDetailDto?> GetAsync(
        long profileId, long mediaItemId, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        var item = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking())
            .SingleOrDefaultAsync(m => m.Id == mediaItemId && m.OwnerProfileId == profileId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var places = (await PlacesOfAsync(profileId, [mediaItemId], cancellationToken)).GetValueOrDefault(mediaItemId) ?? [];
        var dto = (await ToDtosAsync([item], profileId, cancellationToken, new() { [mediaItemId] = places }))[0];
        var isProof = await context.TankDecommissionings.AnyAsync(d => d.StartMediaItemId == mediaItemId || d.EndMediaItemId == mediaItemId, cancellationToken);
        var kept = (await streaks.WeeksKeptByAsync(profileId, mediaItemId, today, utcOffsetMinutes, cancellationToken))
            .Select(k => new MediaStreakWeekDto(k.TankId, k.TankTitle, k.WeekStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        return new MediaLibraryDetailDto(dto, item.OriginalFilename, item.ByteSize, item.CapturedInApp, item.ReceivedAt, item.TakenAt, !isProof, places, kept);
    }

    /// <summary>
    /// The file as a list entry, or null when the member does not own a file with the id.
    /// </summary>
    public async Task<MediaLibraryItemDto?> GetItemAsync(long profileId, long mediaItemId, CancellationToken cancellationToken)
    {
        var item = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking())
            .SingleOrDefaultAsync(m => m.Id == mediaItemId && m.OwnerProfileId == profileId, cancellationToken);
        return item is null ? null : (await ToDtosAsync([item], profileId, cancellationToken))[0];
    }

    /// <summary>
    /// The places that show each of the given files, by media item id, in a stable order. Names and ids are
    /// set for places of the member only.
    /// </summary>
    public async Task<Dictionary<long, List<MediaPlaceDto>>> PlacesOfAsync(
        long profileId, IReadOnlyCollection<long> mediaItemIds, CancellationToken cancellationToken)
    {
        var links = await LiveLinks().AsNoTracking().Where(l => mediaItemIds.Contains(l.MediaItemId))
            .OrderBy(l => l.Id).ToListAsync(cancellationToken);
        if (links.Count == 0)
        {
            return [];
        }

        var careEntries = await LoadAsync(links.Select(l => l.CareEntryId), ids => context.CareEntries.AsNoTracking()
            .Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.TankId, e.Kind, e.OccurredAt }), e => e.Id, cancellationToken);
        var sessions = await LoadAsync(links.Select(l => l.WaterChangeSessionId), ids => context.WaterChangeSessions.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.TankId, s.StartedAt }), s => s.Id, cancellationToken);
        var inhabitants = await LoadAsync(links.Select(l => l.InhabitantId), ids => context.Inhabitants.AsNoTracking()
            .Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.TankId }), i => i.Id, cancellationToken);
        var tankIds = links.Select(l => l.TankId).Concat(careEntries.Values.Select(e => (long?)e.TankId))
            .Concat(sessions.Values.Select(s => (long?)s.TankId)).Concat(inhabitants.Values.Select(i => (long?)i.TankId)).ToList();
        var tanks = await LoadAsync(tankIds, ids => context.Tanks.AsNoTracking()
            .Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.ProfileId, t.Title }), t => t.Id, cancellationToken);
        var stories = await LoadAsync(links.Select(l => l.StoryId), ids => context.Stories.AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.AuthorProfileId, s.Title, s.State }), s => s.Id, cancellationToken);
        var slugs = (await context.SlugAliases.AsNoTracking()
                .Where(a => a.IsCanonical && stories.Keys.Contains(a.StoryId)).Select(a => new { a.StoryId, a.Value }).ToListAsync(cancellationToken))
            .ToDictionary(a => a.StoryId, a => a.Value);
        var posts = await LoadAsync(links.Select(l => l.DiscussionPostId), ids => context.DiscussionPosts.AsNoTracking()
            .Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.AuthorProfileId, ThreadTitle = context.DiscussionThreads.Where(t => t.Id == p.ThreadId).Select(t => t.Title).FirstOrDefault() }),
            p => p.Id, cancellationToken);

        string? OwnTankTitle(long tankId) => tanks.TryGetValue(tankId, out var tank) && tank.ProfileId == profileId ? tank.Title : null;
        long? OwnTankId(long tankId) => tanks.TryGetValue(tankId, out var tank) && tank.ProfileId == profileId ? tankId : null;

        var result = new Dictionary<long, List<MediaPlaceDto>>();
        foreach (var link in links)
        {
            MediaPlaceDto? place = link.ContextType switch
            {
                MediaLinkContext.Tank => new MediaPlaceDto(
                    link.Role switch
                    {
                        MediaLinkRole.Main => MediaPlaceKind.TankMain,
                        MediaLinkRole.Decommission or MediaLinkRole.Recommission => MediaPlaceKind.DecommissionProof,
                        _ => MediaPlaceKind.Tank,
                    },
                    OwnTankTitle(link.TankId!.Value), OwnTankId(link.TankId.Value), null, null, null, MediaLinkRoles.SectionOf(link.Role), null, null),
                MediaLinkContext.Inhabitant when inhabitants.TryGetValue(link.InhabitantId!.Value, out var inhabitant) =>
                    new MediaPlaceDto(MediaPlaceKind.Inhabitant, OwnTankTitle(inhabitant.TankId), OwnTankId(inhabitant.TankId), null, null, null, null, null, null),
                MediaLinkContext.CareEntry when careEntries.TryGetValue(link.CareEntryId!.Value, out var entry) =>
                    new MediaPlaceDto(MediaPlaceKind.CareEntry, OwnTankTitle(entry.TankId), OwnTankId(entry.TankId),
                        OwnTankId(entry.TankId) is null ? null : entry.Id, null, null, null, entry.Kind, entry.OccurredAt),
                MediaLinkContext.WaterChangeSession when sessions.TryGetValue(link.WaterChangeSessionId!.Value, out var session) =>
                    new MediaPlaceDto(MediaPlaceKind.WaterChange, OwnTankTitle(session.TankId), OwnTankId(session.TankId), null, null, null, null, null, session.StartedAt),
                MediaLinkContext.Story when stories.TryGetValue(link.StoryId!.Value, out var story) =>
                    story.AuthorProfileId == profileId
                        ? new MediaPlaceDto(MediaPlaceKind.Story, story.Title, null, null, story.Id,
                            story.State == StoryState.Published ? slugs.GetValueOrDefault(story.Id) : null, null, null, null)
                        : new MediaPlaceDto(MediaPlaceKind.Story, null, null, null, null, null, null, null, null),
                MediaLinkContext.DiscussionPost when posts.TryGetValue(link.DiscussionPostId!.Value, out var post) =>
                    new MediaPlaceDto(MediaPlaceKind.DiscussionPost, post.AuthorProfileId == profileId ? post.ThreadTitle : null, null, null, null, null, null, null, null),
                MediaLinkContext.Profile => new MediaPlaceDto(
                    link.Role == MediaLinkRole.Header ? MediaPlaceKind.ProfileHeader : MediaPlaceKind.ProfileAvatar, null, null, null, null, null, null, null, null),
                _ => null,
            };
            if (place is null)
            {
                continue;
            }

            if (!result.TryGetValue(link.MediaItemId, out var list))
            {
                result[link.MediaItemId] = list = [];
            }

            list.Add(place);
        }

        return result;
    }

    // The links whose context is alive: a tank, a story or a place that belongs to a tank that was not
    // deleted.
    private IQueryable<MediaLink> LiveLinks() => context.MediaLinks.Where(l =>
        (l.ContextType == MediaLinkContext.Tank && context.Tanks.Any(t => t.Id == l.TankId && t.DeletedAt == null))
        || (l.ContextType == MediaLinkContext.Inhabitant
            && context.Inhabitants.Any(i => i.Id == l.InhabitantId && context.Tanks.Any(t => t.Id == i.TankId && t.DeletedAt == null)))
        || (l.ContextType == MediaLinkContext.CareEntry
            && context.CareEntries.Any(e => e.Id == l.CareEntryId && context.Tanks.Any(t => t.Id == e.TankId && t.DeletedAt == null)))
        || (l.ContextType == MediaLinkContext.WaterChangeSession
            && context.WaterChangeSessions.Any(s => s.Id == l.WaterChangeSessionId && context.Tanks.Any(t => t.Id == s.TankId && t.DeletedAt == null)))
        || (l.ContextType == MediaLinkContext.Story && context.Stories.Any(s => s.Id == l.StoryId && s.DeletedAt == null))
        || l.ContextType == MediaLinkContext.DiscussionPost
        || l.ContextType == MediaLinkContext.Profile);

    private static async Task<Dictionary<long, T>> LoadAsync<T>(
        IEnumerable<long?> ids, Func<List<long>, IQueryable<T>> query, Func<T, long> key, CancellationToken cancellationToken)
    {
        var distinct = ids.OfType<long>().Distinct().ToList();
        return distinct.Count == 0 ? [] : (await query(distinct).ToListAsync(cancellationToken)).ToDictionary(key);
    }

    private async Task<List<MediaLibraryItemDto>> ToDtosAsync(
        List<MediaItem> items, long profileId, CancellationToken cancellationToken, Dictionary<long, List<MediaPlaceDto>>? knownPlaces = null)
    {
        var ids = items.Select(i => i.Id).ToList();
        var now = clock.GetUtcNow();
        var places = knownPlaces ?? await PlacesOfAsync(profileId, ids, cancellationToken);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(ids, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(ids, profileId, now, cancellationToken);
        var quickShares = await QuickSharesAsync(items, cancellationToken);
        return items.Select(item =>
        {
            var itemPlaces = places.GetValueOrDefault(item.Id) ?? [];
            long? generation = generations.TryGetValue(item.Id, out var value) ? value : null;
            var visible = anonymousVisible.Contains(item.Id);
            return new MediaLibraryItemDto(
                item.Id, item.Kind, item.CreatedAt, item.Width, item.Height,
                item.Kind == MediaKind.Photo ? _urls.Build(item.Variants, allowSigned: true, visible, generation) : null,
                item.Kind == MediaKind.Video ? VideoQueryService.ToPublicDto(item, objectStore, signer, allowSigned: true, visible, generation) : null,
                itemPlaces.Count, [.. itemPlaces.Select(p => p.Kind).Distinct()], quickShares.GetValueOrDefault(item.Id));
        }).ToList();
    }

    // The story of the quick share of each file, as long as the story is not deleted.
    private async Task<Dictionary<long, MediaQuickShareDto>> QuickSharesAsync(List<MediaItem> items, CancellationToken cancellationToken)
    {
        var storyIds = items.Select(i => i.QuickShareStoryId).OfType<long>().ToList();
        if (storyIds.Count == 0)
        {
            return [];
        }

        var found = await context.Stories.AsNoTracking().Where(s => storyIds.Contains(s.Id) && s.DeletedAt == null)
            .Select(s => new { s.Id, s.State }).ToDictionaryAsync(s => s.Id, cancellationToken);
        var slugs = (await context.SlugAliases.AsNoTracking().Where(a => a.IsCanonical && found.Keys.Contains(a.StoryId))
            .Select(a => new { a.StoryId, a.Value }).ToListAsync(cancellationToken)).ToDictionary(a => a.StoryId, a => a.Value);
        return items.Where(i => i.QuickShareStoryId is { } id && found.ContainsKey(id)).ToDictionary(i => i.Id, i =>
        {
            var story = found[i.QuickShareStoryId!.Value];
            return new MediaQuickShareDto(story.Id, story.State == StoryState.Published ? slugs.GetValueOrDefault(story.Id) : null, story.State);
        });
    }
}
