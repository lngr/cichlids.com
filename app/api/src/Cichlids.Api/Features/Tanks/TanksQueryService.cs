using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Query logic behind the tank list and detail endpoints: the public-visibility rule (published,
/// not deleted), the main-image fallback to the first showcase photo, and the ordered
/// showcase/decoration/technic media sections.
/// </summary>
public sealed class TanksQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariants = new Dictionary<string, string>();
    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore);

    public async Task<PagedResponse<TankListItemDto>> ListAsync(
        long? userId, TankCategory? category, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.Tanks.Where(t => t.State == TankState.Published && t.DeletedAt == null);

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
            .Select(t => new TankProjection(t.Id, t.Title, t.Category, t.MainMediaId, t.ProfileId))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new PagedResponse<TankListItemDto>(total, []);
        }

        var tankIds = page.Select(t => t.Id).ToList();
        var authorIds = page.Select(t => t.ProfileId).Distinct().ToList();

        var imageCounts = await context.TankMedia
            .Where(tm => tankIds.Contains(tm.TankId))
            .GroupBy(tm => tm.TankId)
            .Select(g => new { TankId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TankId, x => x.Count, cancellationToken);

        var mainMediaItemByTank = await ResolveMainMediaItemsAsync(page, cancellationToken);
        var images = await LoadImagesAsync(mainMediaItemByTank.Values, cancellationToken);
        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        var items = page.Select(t => new TankListItemDto(
                t.Id,
                t.Title,
                t.Category,
                mainMediaItemByTank.TryGetValue(t.Id, out var mediaItemId) ? images[mediaItemId] : null,
                imageCounts.GetValueOrDefault(t.Id, 0),
                authors[t.ProfileId].ToDto(objectStore)))
            .ToList();

        return new PagedResponse<TankListItemDto>(total, items);
    }

    public Task<bool> IsVisibleAsync(long id, CancellationToken cancellationToken) =>
        context.Tanks.AnyAsync(t => t.Id == id && t.State == TankState.Published && t.DeletedAt == null, cancellationToken);

    public async Task<TankDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var tank = await context.Tanks
            .FirstOrDefaultAsync(t => t.Id == id && t.State == TankState.Published && t.DeletedAt == null, cancellationToken);

        if (tank is null)
        {
            return null;
        }

        var mediaRows = await context.TankMedia
            .Where(tm => tm.TankId == id)
            .OrderBy(tm => tm.Sort)
            .Select(tm => new TankMediaRow(tm.MediaItemId, tm.Section))
            .ToListAsync(cancellationToken);

        var mainMediaItemByTank = await ResolveMainMediaItemsAsync(
            [new TankProjection(tank.Id, tank.Title, tank.Category, tank.MainMediaId, tank.ProfileId)], cancellationToken);

        IEnumerable<long> sectionMediaItemIds = mediaRows.Select(m => m.MediaItemId);
        var allMediaItemIds = sectionMediaItemIds.Concat(mainMediaItemByTank.Values).Distinct().ToList();
        var images = await LoadImagesAsync(allMediaItemIds, cancellationToken);

        var sections = new TankSectionsDto(
            BuildSection(mediaRows, TankMediaSection.Showcase, images),
            BuildSection(mediaRows, TankMediaSection.Decoration, images),
            BuildSection(mediaRows, TankMediaSection.Technic, images));

        var inhabitants = await LoadInhabitantsAsync(id, cancellationToken);
        var authors = await AuthorBatchLoader.LoadAsync(context, [tank.ProfileId], cancellationToken);

        DimensionsDto? dimensions = tank.WidthValue is null && tank.HeightValue is null && tank.DepthValue is null && tank.DimensionUnit is null
            ? null
            : new DimensionsDto(tank.WidthValue, tank.HeightValue, tank.DepthValue, tank.DimensionUnit);

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
            authors[tank.ProfileId].ToDto(objectStore));
    }

    private static List<TankMediaItemDto> BuildSection(
        IEnumerable<TankMediaRow> mediaRows, TankMediaSection section, IReadOnlyDictionary<long, ImageUrlsDto> images) =>
        mediaRows
            .Where(m => m.Section == section)
            .Select(m => new TankMediaItemDto(m.MediaItemId, images[m.MediaItemId]))
            .ToList();

    /// <summary>
    /// Resolves each tank's main image: its own main_media_id when set, otherwise the first
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

        var fallbackRows = await context.TankMedia
            .Where(tm => tanksNeedingFallback.Contains(tm.TankId) && tm.Section == TankMediaSection.Showcase)
            .Select(tm => new { tm.TankId, tm.MediaItemId, tm.Sort })
            .ToListAsync(cancellationToken);

        foreach (var group in fallbackRows.GroupBy(r => r.TankId))
        {
            result[group.Key] = group.OrderBy(r => r.Sort).First().MediaItemId;
        }

        return result;
    }

    private async Task<Dictionary<long, ImageUrlsDto>> LoadImagesAsync(
        IEnumerable<long> mediaItemIds, CancellationToken cancellationToken)
    {
        var ids = mediaItemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var originalByMediaItem = await context.MediaItems
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StorageKey, cancellationToken);

        var variantsByMediaItem = (await context.MediaVariants
                .Where(v => ids.Contains(v.MediaItemId))
                .Select(v => new { v.MediaItemId, v.Label, v.StorageKey })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.MediaItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Label, x => x.StorageKey));

        return ids.ToDictionary(
            id => id,
            id => _mediaUrlBuilder.Build(originalByMediaItem[id], variantsByMediaItem.GetValueOrDefault(id, NoVariants)));
    }

    private async Task<List<InhabitantDto>> LoadInhabitantsAsync(long tankId, CancellationToken cancellationToken)
    {
        var rows = await context.Inhabitants
            .Where(i => i.TankId == tankId)
            .OrderBy(i => i.Sort)
            .Select(i => new { i.SpeciesId, i.Count })
            .ToListAsync(cancellationToken);

        var speciesIds = rows.Where(r => r.SpeciesId != null).Select(r => r.SpeciesId!.Value).Distinct().ToList();

        var speciesById = await context.Species
            .Where(s => speciesIds.Contains(s.Id))
            .Select(s => new InhabitantSpeciesRefDto(s.Id, s.Slug, s.Genus, s.Name, s.DisplayName))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return rows
            .Select(r => new InhabitantDto(
                r.SpeciesId is { } speciesId ? speciesById.GetValueOrDefault(speciesId) : null, r.Count))
            .ToList();
    }

    private sealed record TankProjection(long Id, string Title, TankCategory? Category, long? MainMediaId, long ProfileId);

    private sealed record TankMediaRow(long MediaItemId, TankMediaSection Section);
}
