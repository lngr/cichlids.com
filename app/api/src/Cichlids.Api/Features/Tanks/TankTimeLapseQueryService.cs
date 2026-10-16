using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
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
public sealed record TankGalleryStoryDto(long Id, string? Slug, string? Title, string? Description, DateTimeOffset PublishedAt, IReadOnlyList<long> MediaItemIds);
public sealed record TankTimeLapseDto(
    bool IsShowcase, IReadOnlyList<TankTimeLapsePhotoDto> Photos, IReadOnlyList<TankGalleryStoryDto> Stories, IReadOnlyList<VideoDto> Videos);

/// <summary>
/// Reads the time-lapse of tanks: photos, gallery stories and videos of the tanks whose profile the viewer
/// sees. A viewer without a profile is anonymous. Photos and videos that anonymous viewers do not see
/// resolve to signed paths only when the caller allows them, which a response cached across viewers
/// never does.
/// </summary>
public sealed class TankTimeLapseQueryService(CichlidsDbContext context, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    public async Task<TankTimeLapseDto?> GetAsync(
        long id, CancellationToken cancellationToken, long? viewerProfileId = null, bool allowSigned = false) =>
        (await GetManyAsync([id], cancellationToken, viewerProfileId, allowSigned)).GetValueOrDefault(id);

    public async Task<Dictionary<long, TankTimeLapseDto>> GetManyAsync(
        IReadOnlyCollection<long> tankIds, CancellationToken cancellationToken, long? viewerProfileId = null, bool allowSigned = false)
    {
        var now = DateTimeOffset.UtcNow;
        var visibleProfiles = context.Profiles.Where(p => p.SuspendedUntil == null || p.SuspendedUntil <= now);
        var tanks = await context.TanksVisibleTo(viewerProfileId).AsNoTracking()
            .Where(t => tankIds.Contains(t.Id) && t.State == TankState.Published && t.DeletedAt == null
                && visibleProfiles.Any(p => p.Id == t.ProfileId))
            .ToListAsync(cancellationToken);
        var idsOfTanks = tanks.Select(t => t.Id).ToList();

        var tankSubjects = await context.StorySubjects.AsNoTracking()
            .Where(s => s.TankId != null && idsOfTanks.Contains(s.TankId.Value))
            .Select(s => new TankSubject(s.StoryId, s.TankId!.Value)).ToListAsync(cancellationToken);
        var subjectStoryIds = tankSubjects.Select(s => s.StoryId).Distinct().ToList();
        var stories = await context.Stories.AsNoTracking()
            .Where(p => subjectStoryIds.Contains(p.Id) && p.State == StoryState.Published && p.DeletedAt == null
                && visibleProfiles.Any(a => a.Id == p.AuthorProfileId && a.Kind == ProfileKind.Member))
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);
        var storyIds = stories.Select(p => p.Id).ToList();
        var storyMedia = await context.MediaLinks.StoryLinks().Where(l => storyIds.Contains(l.StoryId!.Value))
            .OrderBy(l => l.Position).Select(l => new StoryItem(l.StoryId!.Value, l.MediaItemId)).ToListAsync(cancellationToken);
        var directMedia = await context.MediaLinks.TankSectionLinks().Where(l => idsOfTanks.Contains(l.TankId!.Value))
            .Select(l => new TankItem(l.TankId!.Value, l.MediaItemId, l.Role)).ToListAsync(cancellationToken);
        var mainMedia = await context.MediaLinks.TankMainLinks().Where(l => idsOfTanks.Contains(l.TankId!.Value))
            .ToDictionaryAsync(l => l.TankId!.Value, l => l.MediaItemId, cancellationToken);
        var ids = directMedia.Select(tm => tm.MediaItemId).Concat(mainMedia.Values)
            .Concat(storyMedia.Select(pm => pm.MediaItemId)).Distinct().ToList();
        var media = await context.MediaItems.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.Kind == MediaKind.Photo
                && (m.OwnerProfileId == null || visibleProfiles.Any(p => p.Id == m.OwnerProfileId && (p.Kind == ProfileKind.Member || p.Kind == ProfileKind.Provisional))))
            .ToListAsync(cancellationToken);
        var photoIds = media.Select(m => m.Id).ToList();
        var variants = await context.MediaVariants.AsNoTracking().Where(v => photoIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken);
        var builder = new MediaUrlBuilder(objectStore, signer);
        // The visibility rule decides the kind of URL: media that anonymous viewers see get public URLs, every
        // other photo a signed path for the viewer who sees the tank.
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(ids, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(ids, viewerProfileId, now, cancellationToken);
        var images = variants.GroupBy(v => v.MediaItemId).ToDictionary(g => g.Key, g => builder.Build(g, allowSigned, anonymousVisible.Contains(g.Key), generations.TryGetValue(g.Key, out var generation) ? generation : null));
        var storyDates = stories.ToDictionary(p => p.Id, p => p.PublishedAt ?? p.CreatedAt);
        var publicationDates = storyMedia.GroupBy(pm => pm.MediaItemId).ToDictionary(g => g.Key,
            g => g.Min(pm => storyDates[pm.StoryId]));
        var photos = media.Where(m => images.TryGetValue(m.Id, out var image)
                && (image.Large ?? image.Medium ?? image.Small ?? image.Thumb) != null)
            .Select(m => new TankTimeLapsePhotoDto(m.Id, images[m.Id], m.TakenAt,
                publicationDates.GetValueOrDefault(m.Id, m.CreatedAt)))
            .OrderBy(EffectiveDate).ThenBy(p => p.MediaItemId).ToList();
        var slugs = await context.SlugAliases.Where(s => storyIds.Contains(s.StoryId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.StoryId, s => s.Value, cancellationToken);
        var galleryStories = stories.Select(p => new TankGalleryStoryDto(p.Id, slugs.GetValueOrDefault(p.Id), p.Title,
            p.Description, p.PublishedAt ?? p.CreatedAt, storyMedia.Where(pm => pm.StoryId == p.Id)
                .Select(pm => pm.MediaItemId).Where(photoIds.Contains).ToList())).ToList();
        var videoIds = directMedia.Where(m => m.Role == MediaLinkRole.Video).Select(m => m.MediaItemId).ToList();
        var videoItems = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking()
                .Where(m => videoIds.Contains(m.Id) && m.Kind == MediaKind.Video
                    && visibleProfiles.Any(p => p.Id == m.OwnerProfileId && (p.Kind == ProfileKind.Member || p.Kind == ProfileKind.Provisional))))
            .ToListAsync(cancellationToken);
        var readyVideos = videoItems.Select(m => (Item: m, Dto: VideoQueryService.ToPublicDto(m, objectStore, signer, allowSigned,
                anonymousVisible.Contains(m.Id), generations.TryGetValue(m.Id, out var videoGeneration) ? videoGeneration : null)))
            .Where(v => v.Dto.State == VideoState.Ready).ToList();
        var members = await visibleProfiles.Where(p => p.Kind == ProfileKind.Member && tanks.Select(t => t.ProfileId).Contains(p.Id))
            .Select(p => p.Id).ToListAsync(cancellationToken);
        var result = new Dictionary<long, TankTimeLapseDto>();
        foreach (var tank in tanks)
        {
            var ownStories = tankSubjects.Where(s => s.TankId == tank.Id).Select(s => s.StoryId).Intersect(storyIds).ToHashSet();
            var ownIds = directMedia.Where(m => m.TankId == tank.Id).Select(m => m.MediaItemId)
                .Concat(storyMedia.Where(pm => ownStories.Contains(pm.StoryId)).Select(pm => pm.MediaItemId)).ToHashSet();
            if (mainMedia.TryGetValue(tank.Id, out var main)) ownIds.Add(main);
            var ownPhotos = photos.Where(p => ownIds.Contains(p.MediaItemId)).Select(p => p with
            {
                PublishedAt = storyMedia.Where(pm => pm.MediaItemId == p.MediaItemId && ownStories.Contains(pm.StoryId))
                    .Select(pm => storyDates[pm.StoryId]).DefaultIfEmpty(media.First(m => m.Id == p.MediaItemId).CreatedAt).Min()
            }).OrderBy(EffectiveDate).ThenBy(p => p.MediaItemId).ToList();
            result[tank.Id] = new TankTimeLapseDto(IsShowcase(ownPhotos, members.Contains(tank.ProfileId)), ownPhotos,
                galleryStories.Where(p => ownStories.Contains(p.Id)).ToList(),
                readyVideos.Where(v => directMedia.Any(m => m.TankId == tank.Id && m.MediaItemId == v.Item.Id))
                    .OrderBy(v => v.Item.CreatedAt).ThenBy(v => v.Item.Id).Select(v => v.Dto).ToList());
        }
        return result;
    }

    private sealed record StoryItem(long StoryId, long MediaItemId);

    private sealed record TankSubject(long StoryId, long TankId);
    private sealed record TankItem(long TankId, long MediaItemId, MediaLinkRole Role);

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
