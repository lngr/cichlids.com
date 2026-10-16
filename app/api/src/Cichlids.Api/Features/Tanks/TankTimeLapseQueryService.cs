using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// One photo of a tank series. TakenAt is the capture time from the photo's EXIF data as the camera
/// recorded it: the wall-clock time with the recorded offset, or with offset +00:00 when the camera
/// recorded none (the value is then a local time, not a UTC instant). It is null without a
/// plausible capture time.
/// </summary>
public sealed record TankTimeLapsePhotoDto(long MediaItemId, ImageUrlsDto Image, DateTimeOffset? TakenAt, DateTimeOffset PublishedAt);
public sealed record TankStoryPostDto(long Id, string? Slug, string? Title, string? Description, DateTimeOffset PublishedAt, IReadOnlyList<long> MediaItemIds);
public sealed record TankTimeLapseDto(
    bool IsShowcase, IReadOnlyList<TankTimeLapsePhotoDto> Photos, IReadOnlyList<TankStoryPostDto> Posts, IReadOnlyList<VideoDto> Videos);

/// <summary>
/// Reads the time-lapse of tanks: photos, story posts and videos of the tanks whose profile the viewer
/// sees. A viewer without a profile is anonymous. Photos and videos under the private storage prefix
/// resolve to signed paths only when the caller allows them, which a response cached across viewers
/// never does.
/// </summary>
public sealed class TankTimeLapseQueryService(CichlidsDbContext context, IObjectStore objectStore, TankMediaUrlSigner? signer = null)
{
    public async Task<TankTimeLapseDto?> GetAsync(
        long id, CancellationToken cancellationToken, long? viewerProfileId = null, bool allowSigned = false) =>
        (await GetManyAsync([id], cancellationToken, viewerProfileId, allowSigned)).GetValueOrDefault(id);

    public async Task<Dictionary<long, TankTimeLapseDto>> GetManyAsync(
        IReadOnlyCollection<long> tankIds, CancellationToken cancellationToken, long? viewerProfileId = null, bool allowSigned = false)
    {
        var now = DateTimeOffset.UtcNow;
        var visibleProfiles = context.Profiles.Where(p => p.SuspendedUntil == null || p.SuspendedUntil <= now);
        var tanks = await context.Tanks.AsNoTracking().Where(TankVisibilityRules.ProfileVisibleTo(viewerProfileId))
            .Where(t => tankIds.Contains(t.Id) && t.State == TankState.Published && t.DeletedAt == null
                && visibleProfiles.Any(p => p.Id == t.ProfileId))
            .ToListAsync(cancellationToken);
        var idsOfTanks = tanks.Select(t => t.Id).ToList();

        var posts = await context.Posts.AsNoTracking()
            .Where(p => p.TankId != null && idsOfTanks.Contains(p.TankId.Value) && p.State == PostState.Published && p.DeletedAt == null
                && visibleProfiles.Any(a => a.Id == p.AuthorProfileId && a.Kind == ProfileKind.Member))
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);
        var postIds = posts.Select(p => p.Id).ToList();
        var postMedia = await context.PostMedia.Where(pm => postIds.Contains(pm.PostId))
            .OrderBy(pm => pm.Sort).ToListAsync(cancellationToken);
        var directMedia = await context.TankMedia.Where(tm => idsOfTanks.Contains(tm.TankId)).ToListAsync(cancellationToken);
        var ids = directMedia.Select(tm => tm.MediaItemId).Concat(tanks.Where(t => t.MainMediaId != null).Select(t => t.MainMediaId!.Value))
            .Concat(postMedia.Select(pm => pm.MediaItemId)).Distinct().ToList();
        var media = await context.MediaItems.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.Kind == MediaKind.Photo
                && (m.OwnerProfileId == null || visibleProfiles.Any(p => p.Id == m.OwnerProfileId && p.Kind == ProfileKind.Member)))
            .ToListAsync(cancellationToken);
        var photoIds = media.Select(m => m.Id).ToList();
        var variants = await context.MediaVariants.AsNoTracking().Where(v => photoIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken);
        var builder = new MediaUrlBuilder(objectStore, signer);
        // The kind of URL follows the tank. Media that only a tank that is not public holds get no public URL,
        // wherever their objects lie; media a published post also uses keep their own public URLs.
        var nonPublicTanks = tanks.Where(t => !TankVisibilityRules.MediaBelongsInPublicStorage(t.ProfileVisibility)).Select(t => t.Id).ToHashSet();
        var postMediaIds = postMedia.Select(pm => pm.MediaItemId).ToHashSet();
        var privateMediaIds = directMedia.Where(m => nonPublicTanks.Contains(m.TankId)).Select(m => m.MediaItemId)
            .Concat(tanks.Where(t => nonPublicTanks.Contains(t.Id) && t.MainMediaId != null).Select(t => t.MainMediaId!.Value))
            .Where(id => !postMediaIds.Contains(id)).ToHashSet();
        var images = variants.GroupBy(v => v.MediaItemId).ToDictionary(g => g.Key, g => builder.Build(g, allowSigned, privateMediaIds.Contains(g.Key)));
        var postDates = posts.ToDictionary(p => p.Id, p => p.PublishedAt ?? p.CreatedAt);
        var publicationDates = postMedia.GroupBy(pm => pm.MediaItemId).ToDictionary(g => g.Key,
            g => g.Min(pm => postDates[pm.PostId]));
        var photos = media.Where(m => images.TryGetValue(m.Id, out var image)
                && (image.Large ?? image.Medium ?? image.Small ?? image.Thumb) != null)
            .Select(m => new TankTimeLapsePhotoDto(m.Id, images[m.Id], m.TakenAt,
                publicationDates.GetValueOrDefault(m.Id, m.CreatedAt)))
            .OrderBy(EffectiveDate).ThenBy(p => p.MediaItemId).ToList();
        var slugs = await context.SlugAliases.Where(s => postIds.Contains(s.PostId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.PostId, s => s.Value, cancellationToken);
        var story = posts.Select(p => new TankStoryPostDto(p.Id, slugs.GetValueOrDefault(p.Id), p.Title,
            p.Description, p.PublishedAt ?? p.CreatedAt, postMedia.Where(pm => pm.PostId == p.Id)
                .Select(pm => pm.MediaItemId).Where(photoIds.Contains).ToList())).ToList();
        var videoIds = directMedia.Where(m => m.Section == TankMediaSection.Video).Select(m => m.MediaItemId).ToList();
        var videoItems = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking()
                .Where(m => videoIds.Contains(m.Id) && m.Kind == MediaKind.Video
                    && visibleProfiles.Any(p => p.Id == m.OwnerProfileId && p.Kind == ProfileKind.Member)))
            .ToListAsync(cancellationToken);
        var readyVideos = videoItems.Select(m => (Item: m, Dto: VideoQueryService.ToPublicDto(m, objectStore, signer, allowSigned,
                forcePrivate: directMedia.Any(d => d.MediaItemId == m.Id && nonPublicTanks.Contains(d.TankId)))))
            .Where(v => v.Dto.State == VideoState.Ready).ToList();
        var members = await visibleProfiles.Where(p => p.Kind == ProfileKind.Member && tanks.Select(t => t.ProfileId).Contains(p.Id))
            .Select(p => p.Id).ToListAsync(cancellationToken);
        var result = new Dictionary<long, TankTimeLapseDto>();
        foreach (var tank in tanks)
        {
            var ownPosts = posts.Where(p => p.TankId == tank.Id).Select(p => p.Id).ToHashSet();
            var ownIds = directMedia.Where(m => m.TankId == tank.Id).Select(m => m.MediaItemId)
                .Concat(postMedia.Where(pm => ownPosts.Contains(pm.PostId)).Select(pm => pm.MediaItemId)).ToHashSet();
            if (tank.MainMediaId is { } main) ownIds.Add(main);
            var ownPhotos = photos.Where(p => ownIds.Contains(p.MediaItemId)).Select(p => p with
            {
                PublishedAt = postMedia.Where(pm => pm.MediaItemId == p.MediaItemId && ownPosts.Contains(pm.PostId))
                    .Select(pm => postDates[pm.PostId]).DefaultIfEmpty(media.First(m => m.Id == p.MediaItemId).CreatedAt).Min()
            }).OrderBy(EffectiveDate).ThenBy(p => p.MediaItemId).ToList();
            result[tank.Id] = new TankTimeLapseDto(IsShowcase(ownPhotos, members.Contains(tank.ProfileId)), ownPhotos,
                story.Where(p => ownPosts.Contains(p.Id)).ToList(),
                readyVideos.Where(v => directMedia.Any(m => m.TankId == tank.Id && m.MediaItemId == v.Item.Id))
                    .OrderBy(v => v.Item.CreatedAt).ThenBy(v => v.Item.Id).Select(v => v.Dto).ToList());
        }
        return result;
    }

    /// <summary>
    /// The date a photo is placed at in the series: its capture time when it has one, otherwise
    /// its publication or upload date. A capture time without a recorded offset carries offset
    /// zero, so its wall-clock time is compared as if it were UTC.
    /// </summary>
    public static DateTimeOffset EffectiveDate(TankTimeLapsePhotoDto photo) => photo.TakenAt ?? photo.PublishedAt;

    public static bool IsShowcase(IReadOnlyCollection<TankTimeLapsePhotoDto> photos, bool member)
    {
        var dates = photos.Select(EffectiveDate).ToList();
        return member && dates.Count >= 8 && dates.Max() >= dates.Min().AddYears(1)
            && dates.Select(d => (d.Year, d.Month)).Distinct().Count() >= 8;
    }
}
