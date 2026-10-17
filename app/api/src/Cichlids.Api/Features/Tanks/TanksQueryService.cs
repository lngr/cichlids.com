using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Species;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Query logic behind the tank list and detail endpoints: the visibility rule (published, not deleted
/// and a profile level that allows the viewer), the main-image fallback to the first showcase photo, and the
/// ordered showcase/decoration/technic media sections. The owner of a tank always sees it. The photos of
/// a tank are public URLs, or signed paths for photos that live under the private prefix.
/// </summary>
public sealed class TanksQueryService(CichlidsDbContext context, IObjectStore objectStore, MediaUrlSigner? signer = null)
{
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    public async Task<PagedResponse<TankListItemDto>> ListAsync(
        long? userId, TankCategory? category, long? viewerProfileId, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.TanksVisibleTo(viewerProfileId).Where(t => t.State == TankState.Published && t.DeletedAt == null);

        if (userId is { } uid)
        {
            filtered = filtered.Where(t => t.ProfileId == uid);
        }

        if (category is { } cat)
        {
            filtered = filtered.Where(t => t.Category == cat);
        }

        var total = await filtered.CountAsync(cancellationToken);

        var page = await filtered
            .OrderByDescending(t => t.PublishedAt ?? DateTimeOffset.MinValue).ThenByDescending(t => t.Id)
            .Skip(offset).Take(limit)
            .Select(t => new TankProjection(t.Id, t.Title, t.Category,
                context.MediaLinks.Where(l => l.TankId == t.Id && l.Role == MediaLinkRole.Main).Select(l => (long?)l.MediaItemId).FirstOrDefault(),
                t.ProfileId, t.Region, t.ProfileVisibility))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new PagedResponse<TankListItemDto>(total, []);
        }

        var tankIds = page.Select(t => t.Id).ToList();
        var authorIds = page.Select(t => t.ProfileId).Distinct().ToList();

        var imageCounts = await context.MediaLinks.TankSectionLinks()
            .Where(l => tankIds.Contains(l.TankId!.Value) && l.Role != MediaLinkRole.Video)
            .GroupBy(l => l.TankId!.Value)
            .Select(g => new { TankId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TankId, x => x.Count, cancellationToken);

        var mainMediaItemByTank = await ResolveMainMediaItemsAsync(page, cancellationToken);
        var images = await LoadImagesAsync(mainMediaItemByTank.Values, viewerProfileId, cancellationToken);

        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        // A decommissioning is part of the care stream, so only its viewers see the mark.
        var streamVisible = context.TanksStreamVisibleTo(viewerProfileId).Select(t => t.Id);
        var decommissioned = await context.TankDecommissionings
            .Where(d => tankIds.Contains(d.TankId) && d.EndedAt == null && streamVisible.Contains(d.TankId))
            .Select(d => d.TankId).ToListAsync(cancellationToken);

        var items = page.Select(t => new TankListItemDto(
                t.Id,
                t.Title,
                t.Category,
                mainMediaItemByTank.TryGetValue(t.Id, out var mediaItemId) ? images[mediaItemId] : null,
                imageCounts.GetValueOrDefault(t.Id, 0),
                authors[t.ProfileId].ToDto(objectStore), t.Region, t.ProfileVisibility, decommissioned.Contains(t.Id)))
            .ToList();

        return new PagedResponse<TankListItemDto>(total, items);
    }

    public Task<bool> IsVisibleAsync(long id, long? viewerProfileId, CancellationToken cancellationToken) =>
        context.TanksVisibleTo(viewerProfileId)
            .AnyAsync(t => t.Id == id && t.State == TankState.Published && t.DeletedAt == null, cancellationToken);

    public async Task<TankDetailDto?> GetByIdAsync(long id, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var tank = await context.TanksVisibleTo(viewerProfileId)
            .FirstOrDefaultAsync(t => t.Id == id && t.State == TankState.Published && t.DeletedAt == null, cancellationToken);

        if (tank is null)
        {
            return null;
        }

        var mediaRows = await context.MediaLinks.TankSectionLinks()
            .Where(l => l.TankId == id)
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .Select(l => new TankMediaRow(l.MediaItemId, l.Role))
            .ToListAsync(cancellationToken);

        var mainMediaId = await context.MediaLinks.TankMainLinks().Where(l => l.TankId == id)
            .Select(l => (long?)l.MediaItemId).SingleOrDefaultAsync(cancellationToken);
        var mainMediaItemByTank = await ResolveMainMediaItemsAsync(
            [new TankProjection(tank.Id, tank.Title, tank.Category, mainMediaId, tank.ProfileId, tank.Region, tank.ProfileVisibility)], cancellationToken);

        IEnumerable<long> sectionMediaItemIds = mediaRows.Select(m => m.MediaItemId);
        var allMediaItemIds = sectionMediaItemIds.Concat(mainMediaItemByTank.Values).Distinct().ToList();
        var images = await LoadImagesAsync(allMediaItemIds, viewerProfileId, cancellationToken);

        var sections = new TankSectionsDto(
            BuildSection(mediaRows, MediaLinkRole.Showcase, images),
            BuildSection(mediaRows, MediaLinkRole.Decoration, images),
            BuildSection(mediaRows, MediaLinkRole.Technic, images));

        var inhabitants = await LoadInhabitantsAsync(id, cancellationToken);
        var authors = await AuthorBatchLoader.LoadAsync(context, [tank.ProfileId], cancellationToken);

        DimensionsDto? dimensions = tank.WidthValue is null && tank.HeightValue is null && tank.DepthValue is null && tank.DimensionUnit is null
            ? null
            : new DimensionsDto(tank.WidthValue, tank.HeightValue, tank.DepthValue, DimensionUnit.Cm,
                tank.WidthInputValue, tank.HeightInputValue, tank.DepthInputValue, tank.DimensionUnit);

        return new TankDetailDto(
            tank.Id,
            tank.Title,
            tank.Category,
            tank.Description,
            tank.Gravel,
            tank.Plants,
            tank.Decoration,
            tank.Light,
            tank.LightDuration,
            tank.Filtration,
            tank.Technic,
            new WaterValuesDto(tank.WaterPh, tank.WaterKh, tank.WaterGh, tank.WaterNo2, tank.WaterNo3, tank.WaterPo4, tank.WaterNotes),
            tank.Food,
            tank.Notes,
            dimensions,
            mainMediaItemByTank.TryGetValue(tank.Id, out var mainMediaItemId) ? images[mainMediaItemId] : null,
            sections,
            inhabitants,
            authors[tank.ProfileId].ToDto(objectStore),
            tank.VolumeLiters is { } liters ? new VolumeDto(liters, tank.VolumeInputValue, tank.VolumeInputUnit) : null,
            new TankVisibilityDto(tank.ProfileVisibility, tank.StreamVisibility), tank.Region);
    }

    private static List<TankMediaItemDto> BuildSection(
        IEnumerable<TankMediaRow> mediaRows, MediaLinkRole role, IReadOnlyDictionary<long, ImageUrlsDto> images) =>
        mediaRows
            .Where(m => m.Role == role)
            .Select(m => new TankMediaItemDto(m.MediaItemId, images[m.MediaItemId]))
            .ToList();

    /// <summary>
    /// Resolves each tank's main image: the link with the main role when there is one, otherwise the first
    /// showcase photo in section order.
    /// </summary>
    private async Task<Dictionary<long, long>> ResolveMainMediaItemsAsync(
        IReadOnlyCollection<TankProjection> tanks, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, long>();
        var tanksNeedingFallback = new List<long>();

        foreach (var tank in tanks)
        {
            if (tank.MainMediaId is { } mainMediaId)
            {
                result[tank.Id] = mainMediaId;
            }
            else
            {
                tanksNeedingFallback.Add(tank.Id);
            }
        }

        if (tanksNeedingFallback.Count == 0)
        {
            return result;
        }

        var fallbackRows = await context.MediaLinks
            .Where(l => l.TankId != null && tanksNeedingFallback.Contains(l.TankId.Value) && l.Role == MediaLinkRole.Showcase)
            .Select(l => new { TankId = l.TankId!.Value, l.MediaItemId, Sort = l.Position })
            .ToListAsync(cancellationToken);

        foreach (var group in fallbackRows.GroupBy(r => r.TankId))
        {
            result[group.Key] = group.OrderBy(r => r.Sort).First().MediaItemId;
        }

        return result;
    }

    private async Task<Dictionary<long, ImageUrlsDto>> LoadImagesAsync(
        IEnumerable<long> mediaItemIds, long? viewerProfileId, CancellationToken cancellationToken)
    {
        var ids = mediaItemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var variantsByMediaItem = (await context.MediaVariants.AsNoTracking()
                .Where(v => ids.Contains(v.MediaItemId))
                .ToListAsync(cancellationToken))
            .ToLookup(v => v.MediaItemId);

        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(ids, DateTimeOffset.UtcNow, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(ids, viewerProfileId, DateTimeOffset.UtcNow, cancellationToken);
        return ids.ToDictionary(id => id, id => _mediaUrlBuilder.Build(variantsByMediaItem[id], allowSigned: true, anonymousVisible.Contains(id), generations.TryGetValue(id, out var generation) ? generation : null));
    }

    private async Task<List<InhabitantDto>> LoadInhabitantsAsync(long tankId, CancellationToken cancellationToken)
    {
        var rows = await CurrentInhabitants.Query(context).AsNoTracking()
            .Where(i => i.TankId == tankId)
            .OrderBy(i => i.Name == null).ThenBy(i => i.Sort).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
        var dtos = await InhabitantMapper.ToDtosAsync(context, rows, cancellationToken);
        return rows.Select(r => dtos[r.Id]).ToList();
    }

    private sealed record TankProjection(
        long Id, string Title, TankCategory? Category, long? MainMediaId, long ProfileId, TankRegion? Region, TankVisibility ProfileVisibility);

    private sealed record TankMediaRow(long MediaItemId, MediaLinkRole Role);
}
