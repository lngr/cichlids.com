using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// A decommissioning of a tank in the local calendar dates of its owner. The start day belongs to the
/// decommissioning and the end day to the operating time; a span without an end is active.
/// </summary>
public readonly record struct DecommissionSpan(DateOnly Start, DateOnly? End);

/// <summary>
/// The operating time of a tank on a day: the days since its creation without the decommissioned ones,
/// and the whole calendar months of the operating time.
/// </summary>
public readonly record struct TankOperation(int OperatingDays, int DecommissionedDays, int OperatingMonths);

/// <summary>
/// Questions about the decommissionings of a tank, in local calendar dates.
/// </summary>
public static class Decommissions
{
    /// <summary>
    /// Whether a decommissioning of the scope pauses the kind of care.
    /// </summary>
    public static bool Pauses(DecommissionScope scope, CareKind kind) => scope == DecommissionScope.AllCare || kind == CareKind.WaterChange;

    /// <summary>
    /// Whether a decommissioning covers the day.
    /// </summary>
    public static bool Covers(IReadOnlyList<DecommissionSpan> spans, DateOnly day) =>
        spans.Any(s => s.Start <= day && (s.End is not { } end || day < end));

    /// <summary>
    /// The decommissioned days in the range from the first day up to, but not including, the last day.
    /// Each day counts once.
    /// </summary>
    public static int DaysBetween(IReadOnlyList<DecommissionSpan> spans, DateOnly first, DateOnly last)
    {
        var days = new HashSet<int>();
        foreach (var span in spans)
        {
            var from = span.Start > first ? span.Start : first;
            var to = span.End is { } end && end < last ? end : last;
            for (var day = from.DayNumber; day < to.DayNumber; day++) days.Add(day);
        }
        return days.Count;
    }

    /// <summary>
    /// The start of the operating time on a day: the creation date moved forward by the decommissioned
    /// days before that day. A tank year counts its calendar anniversaries from here.
    /// </summary>
    public static DateOnly OperatingStart(DateOnly tankStart, IReadOnlyList<DecommissionSpan> spans, DateOnly day) =>
        day <= tankStart ? tankStart : tankStart.AddDays(DaysBetween(spans, tankStart, day));

    /// <summary>
    /// The operating time of a tank on a day: the days from the creation date up to the day without the
    /// decommissioned ones. The months are the whole calendar months of each stretch of operation, with
    /// the days left over in the stretches added up and counted as months of 30 days.
    /// </summary>
    public static TankOperation OperationOn(DateOnly tankStart, IReadOnlyList<DecommissionSpan> spans, DateOnly day)
    {
        if (day <= tankStart) return new TankOperation(0, 0, 0);
        var months = 0;
        var leftover = 0;
        var operating = 0;
        var cursor = tankStart;
        void Stretch(DateOnly from, DateOnly to)
        {
            if (to <= from) return;
            var whole = WholeMonths(from, to);
            months += whole;
            // A stretch ending after a short month can leave a full month of days; it counts as less than one.
            leftover += Math.Min(to.DayNumber - from.AddMonths(whole).DayNumber, DaysPerLeftoverMonth - 1);
            operating += to.DayNumber - from.DayNumber;
        }
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (span.Start >= day) break;
            Stretch(cursor, span.Start);
            var end = span.End is { } last && last < day ? last : day;
            if (end > cursor) cursor = end;
        }
        Stretch(cursor, day);
        return new TankOperation(operating, DaysBetween(spans, tankStart, day), months + leftover / DaysPerLeftoverMonth);
    }

    private const int DaysPerLeftoverMonth = 30;

    private static int WholeMonths(DateOnly from, DateOnly to) =>
        Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0));

    /// <summary>
    /// The days of the window that the spans cover, ordered by date.
    /// </summary>
    public static IReadOnlyList<DateOnly> DaysIn(IReadOnlyList<DecommissionSpan> spans, DateOnly first, DateOnly last)
    {
        var days = new List<DateOnly>();
        for (var day = first; day <= last; day = day.AddDays(1))
            if (Covers(spans, day)) days.Add(day);
        return days;
    }
}
