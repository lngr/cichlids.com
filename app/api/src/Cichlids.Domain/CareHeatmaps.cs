using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// The number of care entries of one kind on one local calendar day.
/// </summary>
public sealed record CareDayKindCount(DateOnly Date, CareKind Kind, int Count);

/// <summary>
/// The water changes and measurements logged on one local calendar day.
/// </summary>
public sealed record CareDayCount(DateOnly Date, int WaterChanges, int Measurements);

/// <summary>
/// The care days of a window of whole ISO weeks that ends today. Days is sparse: only days with care are
/// listed, ordered by date. LastWaterChange and LastMeasurement are the newest listed days of their kind.
/// </summary>
public sealed record CareHeatmap(DateOnly Start, DateOnly End, IReadOnlyList<CareDayCount> Days, int ActiveDays,
    DateOnly? LastWaterChange, DateOnly? LastMeasurement);

/// <summary>
/// Buckets care entries by the local calendar day of the member into a window of 16 ISO weeks. The window
/// starts on the Monday 15 weeks before the Monday of the current week and ends today, so the last week
/// may be incomplete.
/// </summary>
public static class CareHeatmaps
{
    public const int Weeks = 16;

    /// <summary>
    /// The first day of the window for a local date.
    /// </summary>
    public static DateOnly WindowStart(DateOnly today) => TimeLapseStreaks.WeekStartOf(today).AddDays(-7 * (Weeks - 1));

    /// <summary>
    /// The instants that bound the window for a member whose clock is the given number of minutes ahead of
    /// UTC: From is the start of the first local day and To the start of the day after today, exclusive.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) QueryRange(DateOnly today, int utcOffsetMinutes)
    {
        DateTimeOffset LocalMidnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(-utcOffsetMinutes);
        return (LocalMidnight(WindowStart(today)), LocalMidnight(today.AddDays(1)));
    }

    /// <summary>
    /// Sums rows of the same day and kind and drops the days outside the window.
    /// </summary>
    public static CareHeatmap Build(IEnumerable<CareDayKindCount> rows, DateOnly today)
    {
        var start = WindowStart(today);
        var days = rows.Where(r => r.Date >= start && r.Date <= today)
            .GroupBy(r => r.Date).OrderBy(g => g.Key)
            .Select(g => new CareDayCount(g.Key,
                g.Where(r => r.Kind == CareKind.WaterChange).Sum(r => r.Count),
                g.Where(r => r.Kind == CareKind.Measurement).Sum(r => r.Count)))
            .Where(d => d.WaterChanges + d.Measurements > 0).ToList();
        return new CareHeatmap(start, today, days, days.Count,
            days.Where(d => d.WaterChanges > 0).Select(d => (DateOnly?)d.Date).LastOrDefault(),
            days.Where(d => d.Measurements > 0).Select(d => (DateOnly?)d.Date).LastOrDefault());
    }
}
