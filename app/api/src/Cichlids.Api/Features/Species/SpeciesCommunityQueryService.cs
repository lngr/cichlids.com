using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Stories;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Species;

/// <summary>
/// Query logic behind the community side of a species page. Every figure and card comes from the tanks
/// and stories that anonymous viewers see, owned by members who are not suspended, so the answer is the
/// same for every caller and never exposes a hidden tank.
/// </summary>
public sealed class SpeciesCommunityQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    private const int MaxPhotos = 12;
    private const int MaxTanks = 6;
    private const int MaxPartners = 10;

    /// <summary>
    /// The number of public tanks a partner species needs to appear, so a single tank is never recognisable
    /// from the list.
    /// </summary>
    public const int MinimumPartnerTanks = 3;

    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    public async Task<SpeciesCommunityDto?> GetAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var speciesId = await ResolveSpeciesIdAsync(idOrSlug, cancellationToken);
        if (speciesId is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var owners = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        var publicTanks = context.TanksVisibleTo(null)
            .Where(t => t.State == TankState.Published && t.DeletedAt == null && owners.Any(p => p.Id == t.ProfileId));

        var photos = await LoadPhotosAsync(speciesId.Value, owners, publicTanks, now, cancellationToken);
        var keeping = publicTanks.Where(t => CurrentInhabitants.Query(context).Any(i => i.TankId == t.Id && i.SpeciesId == speciesId));
        var tankCount = await keeping.CountAsync(cancellationToken);
        var tanks = await LoadTanksAsync(keeping, now, cancellationToken);
        var partners = await LoadPartnersAsync(speciesId.Value, keeping, cancellationToken);
        return new SpeciesCommunityDto(photos, tankCount, tanks, partners);
    }

    private async Task<long?> ResolveSpeciesIdAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var id = await context.Species.Where(s => s.Slug == idOrSlug).Select(s => (long?)s.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is null && long.TryParse(idOrSlug, out var numericId))
        {
            id = await context.Species.Where(s => s.Id == numericId).Select(s => (long?)s.Id).FirstOrDefaultAsync(cancellationToken);
        }

        return id;
    }

    /// <summary>
    /// The best published stories that show the species, ordered by rating score, then views. A story
    /// needs a browsable author, a photo that anonymous viewers see, and no tank subject outside the
    /// public tanks.
    /// </summary>
    private async Task<List<SpeciesCommunityPhotoDto>> LoadPhotosAsync(
        long speciesId, IQueryable<Profile> owners, IQueryable<Tank> publicTanks, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var visiblePhotos = context.AnonymousVisibleMediaItems(now).Where(m => m.Kind == MediaKind.Photo);
        var top = await context.Stories.AsNoTracking()
            .Where(p => p.State == StoryState.Published && p.DeletedAt == null
                && (p.Topic == StoryTopic.Cichlids || p.Topic == StoryTopic.Tanks)
                && context.StorySubjects.Any(s => s.StoryId == p.Id && s.SpeciesId == speciesId)
                && owners.Any(a => a.Id == p.AuthorProfileId)
                && !context.StorySubjects.Any(s => s.StoryId == p.Id && s.TankId != null && !publicTanks.Any(t => t.Id == s.TankId))
                && context.MediaLinks.StoryLinks().Any(l => l.StoryId == p.Id && visiblePhotos.Any(m => m.Id == l.MediaItemId)))
            .OrderByDescending(RatingScore.OfStory).ThenByDescending(p => p.ViewCount).ThenByDescending(p => p.Id)
            .Take(MaxPhotos)
            .Select(p => new { p.Id, p.Title, p.ViewCount, p.RatingAverage, p.RatingCount, p.CoverMediaLinkId })
            .ToListAsync(cancellationToken);
        if (top.Count == 0)
        {
            return [];
        }

        var storyIds = top.Select(p => p.Id).ToList();
        var slugs = await context.SlugAliases.AsNoTracking().Where(s => storyIds.Contains(s.StoryId) && s.IsCanonical)
            .ToDictionaryAsync(s => s.StoryId, s => s.Value, cancellationToken);
        var links = await context.MediaLinks.StoryLinks().AsNoTracking()
            .Where(l => storyIds.Contains(l.StoryId!.Value) && visiblePhotos.Any(m => m.Id == l.MediaItemId))
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .Select(l => new { StoryId = l.StoryId!.Value, LinkId = l.Id, l.MediaItemId })
            .ToListAsync(cancellationToken);
        var coverItemByStory = top.ToDictionary(p => p.Id, p =>
        {
            var own = links.Where(l => l.StoryId == p.Id).ToList();
            return (own.FirstOrDefault(l => l.LinkId == p.CoverMediaLinkId) ?? own[0]).MediaItemId;
        });
        var images = await LoadImagesAsync(coverItemByStory.Values.Distinct().ToList(), cancellationToken);

        return top.Select(p => new SpeciesCommunityPhotoDto(
            p.Id, slugs.GetValueOrDefault(p.Id, string.Empty), p.Title, images[coverItemByStory[p.Id]], p.ViewCount, p.RatingAverage, p.RatingCount))
            .ToList();
    }

    /// <summary>
    /// The most recently published of the tanks that keep the species, as cards.
    /// </summary>
    private async Task<List<SpeciesCommunityTankDto>> LoadTanksAsync(
        IQueryable<Tank> keeping, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await keeping.AsNoTracking()
            .OrderByDescending(t => t.PublishedAt).ThenByDescending(t => t.Id)
            .Take(MaxTanks)
            .Select(t => new { t.Id, t.Title, t.Category })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var tankIds = rows.Select(t => t.Id).ToList();
        var imageLinks = await context.MediaLinks.AsNoTracking()
            .Where(l => l.TankId != null && tankIds.Contains(l.TankId.Value)
                && (l.Role == MediaLinkRole.Main || l.Role == MediaLinkRole.Showcase)
                && context.AnonymousVisibleMediaItems(now).Any(m => m.Id == l.MediaItemId && m.Kind == MediaKind.Photo))
            .Select(l => new { TankId = l.TankId!.Value, l.MediaItemId, l.Role, l.Position, l.Id })
            .ToListAsync(cancellationToken);
        var itemByTank = imageLinks.GroupBy(l => l.TankId).ToDictionary(g => g.Key,
            g => g.OrderBy(l => l.Role == MediaLinkRole.Main ? 0 : 1).ThenBy(l => l.Position).ThenBy(l => l.Id).First().MediaItemId);
        var images = await LoadImagesAsync(itemByTank.Values.Distinct().ToList(), cancellationToken);

        return rows.Select(t => new SpeciesCommunityTankDto(
            t.Id, t.Title, t.Category, itemByTank.TryGetValue(t.Id, out var item) ? images[item] : null)).ToList();
    }

    /// <summary>
    /// The species kept in the same tanks as the requested one, counted in distinct tanks, with at least
    /// the minimum number of tanks, most common first.
    /// </summary>
    private async Task<List<SpeciesStockingPartnerDto>> LoadPartnersAsync(
        long speciesId, IQueryable<Tank> keeping, CancellationToken cancellationToken)
    {
        var counted = await CurrentInhabitants.Query(context).AsNoTracking()
            .Where(i => i.SpeciesId != null && i.SpeciesId != speciesId && keeping.Any(t => t.Id == i.TankId))
            .GroupBy(i => i.SpeciesId!.Value)
            .Select(g => new { SpeciesId = g.Key, Tanks = g.Select(i => i.TankId).Distinct().Count() })
            .Where(g => g.Tanks >= MinimumPartnerTanks)
            .OrderByDescending(g => g.Tanks).ThenBy(g => g.SpeciesId)
            .Take(MaxPartners)
            .ToListAsync(cancellationToken);
        if (counted.Count == 0)
        {
            return [];
        }

        var ids = counted.Select(c => c.SpeciesId).ToList();
        var species = await context.Species.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        return counted.Select(c =>
        {
            var s = species[c.SpeciesId];
            return new SpeciesStockingPartnerDto(new InhabitantSpeciesRefDto(s.Id, s.Slug, s.Genus, s.Name, s.DisplayName), c.Tanks);
        }).ToList();
    }

    /// <summary>
    /// The public image URLs of media items that anonymous viewers see, one query for all items.
    /// </summary>
    private async Task<Dictionary<long, ImageUrlsDto>> LoadImagesAsync(IReadOnlyCollection<long> mediaItemIds, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Count == 0)
        {
            return [];
        }

        var variants = (await context.MediaVariants.AsNoTracking().Where(v => mediaItemIds.Contains(v.MediaItemId))
            .ToListAsync(cancellationToken)).ToLookup(v => v.MediaItemId);
        return mediaItemIds.ToDictionary(id => id, id => _mediaUrlBuilder.Build(variants[id], allowSigned: false, anonymousVisible: true, issuableGeneration: null));
    }
}
