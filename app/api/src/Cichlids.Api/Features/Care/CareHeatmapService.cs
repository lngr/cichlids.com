using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Counts the care entries of tanks per local calendar day over the heatmap window with one grouped
/// query, so the cost does not depend on how many entries a tank has.
/// </summary>
public sealed class CareHeatmapService(CichlidsDbContext context)
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The heatmap of one tank. Entries marked only me count when includeOnlyMe is true.
    /// </summary>
    public Task<CareHeatmapDto> GetTankAsync(long tankId, bool includeOnlyMe, DateOnly today, int utcOffsetMinutes, CancellationToken ct) =>
        BuildAsync(context.CareEntries.AsNoTracking().Where(e => e.TankId == tankId && (includeOnlyMe || !e.OnlyMe)), today, utcOffsetMinutes, ct);

    /// <summary>
    /// The heatmap of the live tanks of a member, summed, with every entry of the member.
    /// </summary>
    public Task<CareHeatmapDto> GetOwnAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken ct) =>
        BuildAsync(context.CareEntries.AsNoTracking()
            .Where(e => context.Tanks.Any(t => t.Id == e.TankId && t.ProfileId == profileId && t.DeletedAt == null)), today, utcOffsetMinutes, ct);

    private static async Task<CareHeatmapDto> BuildAsync(IQueryable<CareEntry> entries, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var (from, to) = CareHeatmaps.QueryRange(today, utcOffsetMinutes);
        var rows = await entries.Where(e => e.OccurredAt >= from && e.OccurredAt < to)
            .Select(e => new { Local = e.OccurredAt.UtcDateTime.AddMinutes(utcOffsetMinutes), e.Kind })
            .GroupBy(e => new { e.Local.Year, e.Local.Month, e.Local.Day, e.Kind })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Kind, Count = g.Count() })
            .ToListAsync(ct);
        var map = CareHeatmaps.Build(rows.Select(r => new CareDayKindCount(new DateOnly(r.Year, r.Month, r.Day), r.Kind, r.Count)), today);
        return new CareHeatmapDto(Format(map.Start), Format(map.End), CareHeatmaps.Weeks,
            map.Days.Select(d => new CareHeatmapDayDto(Format(d.Date), d.WaterChanges, d.Measurements)).ToList(),
            map.ActiveDays, map.LastWaterChange is { } water ? Format(water) : null,
            map.LastMeasurement is { } measurement ? Format(measurement) : null);
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);
}
