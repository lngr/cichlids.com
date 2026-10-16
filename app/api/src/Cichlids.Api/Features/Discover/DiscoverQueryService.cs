using System.Runtime.CompilerServices;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cichlids.Api.Features.Discover;

public sealed record DiscoverItemDto(long Id, string? Title, string? Slug, ImageUrlsDto? Image, double Score, int RatingCount);
public sealed record DiscoverRegionDto(TankRegion Region, IReadOnlyList<DiscoverItemDto> Tanks,
    IReadOnlyList<DiscoverItemDto> Pictures, DiscoverItemDto? TankOfTheWeek, IReadOnlyList<DiscoverItemDto> TimeLapses);
public sealed record DiscoverDto(IReadOnlyList<DiscoverRegionDto> Regions, IReadOnlyList<DiscoverItemDto> GlobalPictures);

public sealed class DiscoverQueryService(CichlidsDbContext context, TankTimeLapseQueryService series,
    IObjectStore objectStore, IMemoryCache cache, TimeProvider clock)
{
    private static readonly ConditionalWeakTable<IMemoryCache, SemaphoreSlim> ComputationLocks = new();

    public static readonly TankRegion[] SelectableRegions = Enum.GetValues<TankRegion>().Where(r => r != TankRegion.AmericasUnassigned).ToArray();

    public async Task<DiscoverDto> GetAsync(IReadOnlyCollection<TankRegion> regions, CancellationToken cancellationToken)
    {
        var selected = regions.Distinct().Order().ToArray();
        var now = clock.GetUtcNow();
        var key = $"discover:{System.Globalization.ISOWeek.GetYear(now.UtcDateTime)}:{System.Globalization.ISOWeek.GetWeekOfYear(now.UtcDateTime)}";
        if (!cache.TryGetValue<DiscoverDto>(key, out var archive))
        {
            var gate = ComputationLocks.GetValue(cache, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (!cache.TryGetValue(key, out archive))
                {
                    archive = await ComputeAsync(now, cancellationToken);
                    cache.Set(key, archive, TimeSpan.FromMinutes(15));
                }
            }
            finally
            {
                gate.Release();
            }
        }
        return await VisibleAsync(archive!, selected, now, cancellationToken);
    }

    /// <summary>
    /// Filters the cached rankings against the current visibility, so a deletion, an unpublished
    /// item or a suspension takes effect at once while the rankings themselves stay cached.
    /// </summary>
    /// <summary>
    /// The tanks Discover may show to anyone: published, not deleted, owned by a browsable member and
    /// with a public profile. Every Discover output, whether computed or filtered at read time, takes
    /// its tanks from this one query.
    /// </summary>
    private IQueryable<Cichlids.Domain.Entities.Tank> PublicTanks(IQueryable<Cichlids.Domain.Entities.Profile> members) =>
        context.Tanks.Where(TankVisibilityRules.ProfileVisibleTo(null))
            .Where(t => t.State == TankState.Published && t.DeletedAt == null && members.Any(p => p.Id == t.ProfileId));

    /// <summary>
    /// Restricts posts to those Discover may show: a post linked to a tank needs that tank to be one of
    /// the public tanks.
    /// </summary>
    private static IQueryable<Cichlids.Domain.Entities.Post> OfPublicTanks(
        IQueryable<Cichlids.Domain.Entities.Post> posts, IQueryable<Cichlids.Domain.Entities.Tank> publicTanks) =>
        posts.Where(p => p.TankId == null || publicTanks.Any(t => t.Id == p.TankId));

    private async Task<DiscoverDto> VisibleAsync(DiscoverDto archive, TankRegion[] selected, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var regions = archive.Regions.Where(r => selected.Contains(r.Region)).ToList();
        var tankIds = regions.SelectMany(r => r.Tanks.Concat(r.TimeLapses)).Select(t => t.Id)
            .Concat(regions.Where(r => r.TankOfTheWeek != null).Select(r => r.TankOfTheWeek!.Id)).Distinct().ToList();
        var postIds = regions.SelectMany(r => r.Pictures).Concat(archive.GlobalPictures).Select(p => p.Id).Distinct().ToList();
        var members = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        var publicTanks = PublicTanks(members);
        var visibleTanks = (await publicTanks.Where(t => tankIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var visiblePosts = (await OfPublicTanks(context.Posts.Where(p => postIds.Contains(p.Id) && p.State == PostState.Published
                && p.DeletedAt == null && members.Any(a => a.Id == p.AuthorProfileId)), publicTanks)
            .Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();
        return new DiscoverDto(regions.Select(region =>
        {
            var hall = region.Tanks.Where(t => visibleTanks.Contains(t.Id)).ToList();
            return region with
            {
                Tanks = hall,
                Pictures = region.Pictures.Where(p => visiblePosts.Contains(p.Id)).ToList(),
                TankOfTheWeek = hall.Count == 0 ? null : hall[DiscoverRanking.WeeklyIndex(now, hall.Count)],
                TimeLapses = region.TimeLapses.Where(t => visibleTanks.Contains(t.Id)).ToList(),
            };
        }).ToList(), archive.GlobalPictures.Where(p => visiblePosts.Contains(p.Id)).ToList());
    }

    private async Task<DiscoverDto> ComputeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var selected = SelectableRegions;
        var members = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        var tanks = PublicTanks(members).AsNoTracking();
        var posts = OfPublicTanks(context.Posts.AsNoTracking().Where(p => p.State == PostState.Published && p.DeletedAt == null
            && members.Any(a => a.Id == p.AuthorProfileId)
            && context.PostMedia.Any(pm => pm.PostId == p.Id && context.MediaItems.Any(m => m.Id == pm.MediaItemId && m.Kind == MediaKind.Photo))), tanks);
        var tankRatings = context.Ratings.Where(r => tanks.Any(t => t.Id == r.TankId && (r.ProfileId == null || r.ProfileId != t.ProfileId)));
        var pictureRatings = context.Ratings.Where(r => posts.Any(p => p.Id == r.PostId && (r.ProfileId == null || r.ProfileId != p.AuthorProfileId)));
        var tr = await tankRatings.GroupBy(r => r.TankId!.Value).Select(g => new Stats(g.Key, g.Count(), g.Sum(r => (long)r.Stars))).ToListAsync(cancellationToken);
        var pr = await pictureRatings.GroupBy(r => r.PostId!.Value).Select(g => new Stats(g.Key, g.Count(), g.Sum(r => (long)r.Stars))).ToListAsync(cancellationToken);
        var tankPrior = Prior(tr);
        var picturePrior = Prior(pr);
        var tankStats = tr.ToDictionary(r => r.Id);
        var pictureStats = pr.ToDictionary(r => r.Id);
        var tankRows = await tanks.ToListAsync(cancellationToken);
        var regionalTanks = tankRows.Where(t => selected.Any(r => DiscoverRanking.InRegion(t.Region, r))).ToList();
        var ratedPostIds = pr.Where(r => r.Count >= DiscoverRanking.MinimumRatings).Select(r => r.Id).ToList();
        var tankIds = regionalTanks.Select(t => t.Id).ToList();
        var pictureRows = await posts.Where(p => ratedPostIds.Contains(p.Id))
            .Select(p => new Picture(p.Id, p.Title, p.TankId)).ToListAsync(cancellationToken);
        var tankRegions = tankRows.ToDictionary(t => t.Id, t => t.Region);
        var speciesCategories = await (from ps in context.PostSpecies
            join species in context.Species on ps.SpeciesId equals species.Id
            where ratedPostIds.Contains(ps.PostId)
            select new { ps.PostId, species.Category }).ToListAsync(cancellationToken);
        var speciesRegions = speciesCategories.GroupBy(s => s.PostId).ToDictionary(g => g.Key,
            g => g.Select(s => DiscoverRanking.SpeciesRegion(s.Category)).Distinct().ToArray());
        bool InPictureRegion(Picture picture, TankRegion region) => picture.TankId is { } id
            ? DiscoverRanking.InRegion(tankRegions.GetValueOrDefault(id), region)
            : speciesRegions.TryGetValue(picture.Id, out var origins) && origins.Any(origin => DiscoverRanking.InRegion(origin, region));
        var rankedPictures = pictureRows.OrderByDescending(p => DiscoverRanking.Score(pictureStats[p.Id].Sum, pictureStats[p.Id].Count, picturePrior))
            .ThenBy(p => p.Id).ToList();
        var globalPictures = rankedPictures.Take(20).ToList();
        var postIds = selected.SelectMany(region => rankedPictures.Where(p => InPictureRegion(p, region)).Take(20))
            .Concat(globalPictures).Select(p => p.Id).Distinct().ToList();
        var pictureSlugs = await context.SlugAliases.Where(s => postIds.Contains(s.PostId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.PostId, s => s.Value, cancellationToken);
        var postMedia = await context.PostMedia.Where(pm => postIds.Contains(pm.PostId) && context.MediaItems.Any(m => m.Id == pm.MediaItemId && m.Kind == MediaKind.Photo)).OrderBy(pm => pm.Sort).ThenBy(pm => pm.Id).ToListAsync(cancellationToken);
        var tankMedia = await context.TankMedia.Where(tm => tankIds.Contains(tm.TankId) && tm.Section == TankMediaSection.Showcase)
            .OrderBy(tm => tm.Sort).ThenBy(tm => tm.Id).ToListAsync(cancellationToken);
        var mediaIds = postMedia.Select(pm => pm.MediaItemId).Concat(tankMedia.Select(tm => tm.MediaItemId))
            .Concat(regionalTanks.Where(t => t.MainMediaId != null).Select(t => t.MainMediaId!.Value)).Distinct().ToList();
        var variants = await context.MediaVariants.AsNoTracking().Where(v => mediaIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken);
        // The result is cached across viewers, so it holds public URLs only and no signed path.
        var builder = new MediaUrlBuilder(objectStore);
        var images = variants.GroupBy(v => v.MediaItemId).ToDictionary(g => g.Key, g => builder.Build(g, allowSigned: false));
        var visiblePhotos = context.MediaItems.Where(m => m.Kind == MediaKind.Photo
            && (m.OwnerProfileId == null || members.Any(p => p.Id == m.OwnerProfileId)));
        var photoLinks = context.TankMedia.Where(tm => tm.Section != TankMediaSection.Decommission && tm.Section != TankMediaSection.Recommission)
            .Select(tm => new { tm.TankId, tm.MediaItemId })
            .Union(context.Tanks.Where(t => t.MainMediaId != null)
                .Select(t => new { TankId = t.Id, MediaItemId = t.MainMediaId!.Value }))
            .Union(from p in posts
                join pm in context.PostMedia on p.Id equals pm.PostId
                where p.TankId != null
                select new { TankId = p.TankId!.Value, pm.MediaItemId });
        var candidateIds = await photoLinks.Where(link => tankIds.Contains(link.TankId)
                && visiblePhotos.Any(m => m.Id == link.MediaItemId))
            .GroupBy(link => link.TankId).Where(g => g.Count() >= 8)
            .Select(g => g.Key).ToListAsync(cancellationToken);
        var showcases = new Dictionary<long, TankTimeLapseDto>();
        foreach (var chunk in candidateIds.Chunk(100))
        {
            foreach (var (id, timeLapse) in await series.GetManyAsync(chunk, cancellationToken))
                if (timeLapse.IsShowcase) showcases[id] = timeLapse;
        }
        DiscoverItemDto TankCard(Cichlids.Domain.Entities.Tank t)
        {
            var stats = tankStats.GetValueOrDefault(t.Id) ?? new Stats(t.Id, 0, 0);
            var imageId = t.MainMediaId ?? tankMedia.FirstOrDefault(tm => tm.TankId == t.Id)?.MediaItemId;
            return new(t.Id, t.Title, null, imageId is { } id ? images.GetValueOrDefault(id) : null,
                DiscoverRanking.Score(stats.Sum, stats.Count, tankPrior), stats.Count);
        }
        DiscoverItemDto PictureCard(Picture picture) => new(picture.Id, picture.Title, pictureSlugs.GetValueOrDefault(picture.Id),
            postMedia.FirstOrDefault(pm => pm.PostId == picture.Id) is { } pm ? images.GetValueOrDefault(pm.MediaItemId) : null,
            DiscoverRanking.Score(pictureStats[picture.Id].Sum, pictureStats[picture.Id].Count, picturePrior), pictureStats[picture.Id].Count);
        var result = selected.Select(region =>
        {
            var ranked = regionalTanks.Where(t => DiscoverRanking.InRegion(t.Region, region)).Select(TankCard)
                .OrderByDescending(t => t.Score).ThenBy(t => t.Id).ToList();
            var hall = ranked.Where(t => t.RatingCount >= DiscoverRanking.MinimumRatings).Take(20).ToList();
            var pictures = rankedPictures.Where(p => InPictureRegion(p, region)).Take(20).Select(PictureCard).ToList();
            var timeLapses = ranked.Where(t => showcases.ContainsKey(t.Id)).Take(20).Select(t => t with
            {
                Image = t.Image ?? showcases[t.Id].Photos[0].Image
            }).ToList();
            return new DiscoverRegionDto(region, hall, pictures, hall.Count == 0 ? null : hall[DiscoverRanking.WeeklyIndex(now, hall.Count)], timeLapses);
        }).ToList();
        return new DiscoverDto(result, globalPictures.Select(PictureCard).ToList());
    }

    private static double Prior(IReadOnlyCollection<Stats> stats) => stats.Count == 0 ? 0 : (double)stats.Sum(s => s.Sum) / stats.Sum(s => s.Count);
    private sealed record Picture(long Id, string? Title, long? TankId);
    private sealed record Stats(long Id, int Count, long Sum);
}
