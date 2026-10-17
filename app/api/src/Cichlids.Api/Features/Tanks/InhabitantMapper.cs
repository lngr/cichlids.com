using System.Globalization;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Maps inhabitants to their DTOs with one query for the species of all rows.
/// </summary>
internal static class InhabitantMapper
{
    public static async Task<Dictionary<long, InhabitantDto>> ToDtosAsync(
        CichlidsDbContext context, IReadOnlyCollection<Inhabitant> rows, CancellationToken cancellationToken)
    {
        var speciesIds = rows.Where(r => r.SpeciesId != null).Select(r => r.SpeciesId!.Value).Distinct().ToList();
        var speciesById = await context.Species.AsNoTracking()
            .Where(s => speciesIds.Contains(s.Id))
            .Select(s => new InhabitantSpeciesRefDto(s.Id, s.Slug, s.Genus, s.Name, s.DisplayName))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        return rows.ToDictionary(r => r.Id, r => new InhabitantDto(
            r.Id, r.SpeciesId is { } speciesId ? speciesById.GetValueOrDefault(speciesId) : null, r.Count, r.Name,
            Format(r.MovedInOn), Format(r.RemovedOn), r.RemovalReason));
    }

    private static string? Format(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
