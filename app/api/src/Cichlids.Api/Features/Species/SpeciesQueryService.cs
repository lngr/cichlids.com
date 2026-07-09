using Cichlids.Api.Features.Common;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Species;

/// <summary>
/// Query logic behind the species catalog endpoints: free-text filtering across genus, species
/// name and display name, and resolution of a route segment that may be either a numeric id or a
/// slug.
/// </summary>
public sealed class SpeciesQueryService(CichlidsDbContext context)
{
    public async Task<PagedResponse<SpeciesListItemDto>> ListAsync(
        string? query, int offset, int limit, CancellationToken cancellationToken)
    {
        var filtered = context.Species.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query}%";
            filtered = filtered.Where(s =>
                EF.Functions.ILike(s.Genus, pattern) ||
                EF.Functions.ILike(s.Name, pattern) ||
                EF.Functions.ILike(s.DisplayName, pattern));
        }

        var total = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderBy(s => s.Genus).ThenBy(s => s.Name)
            .Skip(offset).Take(limit)
            .Select(s => new SpeciesListItemDto(s.Id, s.Slug, s.Genus, s.Name, s.DisplayName, s.Category))
            .ToListAsync(cancellationToken);

        return new PagedResponse<SpeciesListItemDto>(total, items);
    }

    /// <summary>
    /// Resolves a species by slug first, falling back to a numeric id when the segment is not a
    /// known slug, so both forms of the route work without a separate endpoint each.
    /// </summary>
    public async Task<SpeciesDetailDto?> GetByIdOrSlugAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var species = await context.Species.FirstOrDefaultAsync(s => s.Slug == idOrSlug, cancellationToken);

        if (species is null && long.TryParse(idOrSlug, out var id))
        {
            species = await context.Species.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        if (species is null)
        {
            return null;
        }

        var commonNames = await context.SpeciesCommonNames
            .Where(n => n.SpeciesId == species.Id)
            .OrderBy(n => n.Name)
            .Select(n => n.Name)
            .ToListAsync(cancellationToken);

        var links = await context.SpeciesLinks
            .Where(l => l.SpeciesId == species.Id)
            .OrderBy(l => l.Sort)
            .Select(l => new SpeciesLinkDto(l.Url, l.Label))
            .ToListAsync(cancellationToken);

        return new SpeciesDetailDto(
            species.Id, species.Slug, species.Genus, species.Name, species.DisplayName, species.Category,
            species.TemperatureRange, species.PhRange, species.GhRange, species.KhRange, species.MaxSize,
            species.Breeding, species.Aggression, species.IntraAggression, species.Diet,
            commonNames, links, species.Description, species.Origin, species.Habitat, species.Morphs);
    }
}
