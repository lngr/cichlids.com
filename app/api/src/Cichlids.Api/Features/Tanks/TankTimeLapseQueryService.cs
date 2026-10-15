using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

public sealed record TankTimeLapsePhotoDto(long MediaItemId, ImageUrlsDto Image, DateTimeOffset? TakenAt, DateTimeOffset PublishedAt);
public sealed record TankStoryPostDto(long Id, string? Slug, string? Title, string? Description, DateTimeOffset PublishedAt, IReadOnlyList<long> MediaItemIds);
public sealed record TankTimeLapseDto(bool IsShowcase, IReadOnlyList<TankTimeLapsePhotoDto> Photos, IReadOnlyList<TankStoryPostDto> Posts);

public sealed class TankTimeLapseQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    public async Task<TankTimeLapseDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var visibleProfiles = context.Profiles.Where(p => p.SuspendedUntil == null || p.SuspendedUntil <= now);
        var tank = await context.Tanks.AsNoTracking()
            .Where(t => t.Id == id && t.State == TankState.Published && t.DeletedAt == null
                && visibleProfiles.Any(p => p.Id == t.ProfileId))
            .FirstOrDefaultAsync(cancellationToken);
        if (tank is null) return null;

        var posts = await context.Posts.AsNoTracking()
            .Where(p => p.TankId == id && p.State == PostState.Published && p.DeletedAt == null
                && visibleProfiles.Any(a => a.Id == p.AuthorProfileId && a.Kind == ProfileKind.Member))
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);
        var postIds = posts.Select(p => p.Id).ToList();
        var postMedia = await context.PostMedia.Where(pm => postIds.Contains(pm.PostId))
            .OrderBy(pm => pm.Sort).ToListAsync(cancellationToken);
        var directIds = await context.TankMedia.Where(tm => tm.TankId == id)
            .Select(tm => tm.MediaItemId).ToListAsync(cancellationToken);
        if (tank.MainMediaId is { } mainId) directIds.Add(mainId);
        var ids = directIds.Concat(postMedia.Select(pm => pm.MediaItemId)).Distinct().ToList();
        var media = await context.MediaItems.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.Kind == MediaKind.Photo
                && (m.OwnerProfileId == null || visibleProfiles.Any(p => p.Id == m.OwnerProfileId && p.Kind == ProfileKind.Member)))
            .ToListAsync(cancellationToken);
        var photoIds = media.Select(m => m.Id).ToList();
        var variants = await context.MediaVariants.Where(v => photoIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken);
        var builder = new MediaUrlBuilder(objectStore);
        var images = variants.GroupBy(v => v.MediaItemId).ToDictionary(g => g.Key,
            g => builder.Build(g.ToDictionary(v => v.Label, v => v.StorageKey)));
        var postDates = posts.ToDictionary(p => p.Id, p => p.PublishedAt ?? p.CreatedAt);
        var publicationDates = postMedia.GroupBy(pm => pm.MediaItemId).ToDictionary(g => g.Key,
            g => g.Min(pm => postDates[pm.PostId]));
        var photos = media.Where(m => images.TryGetValue(m.Id, out var image)
                && (image.Large ?? image.Medium ?? image.Small ?? image.Thumb) != null)
            .Select(m => new TankTimeLapsePhotoDto(m.Id, images[m.Id], null,
                publicationDates.GetValueOrDefault(m.Id, m.CreatedAt)))
            .OrderBy(p => p.PublishedAt).ThenBy(p => p.MediaItemId).ToList();
        var slugs = await context.SlugAliases.Where(s => postIds.Contains(s.PostId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.PostId, s => s.Value, cancellationToken);
        var story = posts.Select(p => new TankStoryPostDto(p.Id, slugs.GetValueOrDefault(p.Id), p.Title,
            p.Description, p.PublishedAt ?? p.CreatedAt, postMedia.Where(pm => pm.PostId == p.Id)
                .Select(pm => pm.MediaItemId).Where(photoIds.Contains).ToList())).ToList();
        var dates = photos.Select(p => p.TakenAt ?? p.PublishedAt).ToList();
        var qualifies = dates.Count >= 8 && dates.Max() >= dates.Min().AddYears(1)
            && dates.Select(d => (d.Year, d.Month)).Distinct().Count() >= 8
            && await visibleProfiles.AnyAsync(p => p.Id == tank.ProfileId && p.Kind == ProfileKind.Member, cancellationToken);
        return new TankTimeLapseDto(qualifies, photos, story);
    }
}
