using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Counts the care entries of tanks per local calendar day over the heatmap window with one grouped
/// query, so the cost does not depend on how many entries a tank has. The days on which a decommissioning
/// pauses all care are listed apart from the counts.
/// </summary>
public sealed class CareHeatmapService(CichlidsDbContext context)
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The heatmap of one tank. Entries marked only me count when includeOnlyMe is true.
    /// </summary>
    public async Task<CareHeatmapDto> GetTankAsync(long tankId, bool includeOnlyMe, DateOnly today, int utcOffsetMinutes, CancellationToken ct) =>
        await BuildAsync(context.CareEntries.AsNoTracking().Where(e => e.TankId == tankId && (includeOnlyMe || !e.OnlyMe)), today, utcOffsetMinutes,
            await RestingDaysAsync([tankId], today, ct), ct);

    /// <summary>
    /// The heatmap of the live tanks of a member, summed, with every entry of the member. It marks the days
    /// on which all live tanks of the member rest.
    /// </summary>
    public async Task<CareHeatmapDto> GetOwnAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken ct) =>
        await BuildAsync(context.CareEntries.AsNoTracking()
            .Where(e => context.Tanks.Any(t => t.Id == e.TankId && t.ProfileId == profileId && t.DeletedAt == null)), today, utcOffsetMinutes,
            await RestingDaysAsync(await context.Tanks.AsNoTracking().Where(t => t.ProfileId == profileId && t.DeletedAt == null).Select(t => t.Id).ToListAsync(ct), today, ct), ct);

    // The days of the window on which every given tank has all of its care paused.
    private async Task<IReadOnlyList<string>> RestingDaysAsync(IReadOnlyCollection<long> tankIds, DateOnly today, CancellationToken ct)
    {
        if (tankIds.Count == 0) return [];
        var rows = (await context.TankDecommissionings.AsNoTracking().Where(d => tankIds.Contains(d.TankId) && d.Scope == DecommissionScope.AllCare)
            .Select(d => new { d.TankId, d.StartDate, d.EndDate }).ToListAsync(ct)).ToLookup(d => d.TankId);
        var start = CareHeatmaps.WindowStart(today);
        IEnumerable<DateOnly>? resting = null;
        foreach (var tank in tankIds)
        {
            var days = Decommissions.DaysIn(rows[tank].Select(d => new DecommissionSpan(d.StartDate, d.EndDate)).ToList(), start, today);
            resting = resting is null ? days : resting.Intersect(days);
        }
        return resting!.Select(Format).ToList();
    }

    private static async Task<CareHeatmapDto> BuildAsync(IQueryable<CareEntry> entries, DateOnly today, int utcOffsetMinutes,
        IReadOnlyList<string> decommissionedDays, CancellationToken ct)
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
            map.LastMeasurement is { } measurement ? Format(measurement) : null, decommissionedDays);
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);
}
