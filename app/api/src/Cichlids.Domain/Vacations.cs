namespace Cichlids.Domain;

/// <summary>
/// A vacation of a member in the member's local calendar dates. The first and the last day belong to
/// the vacation; a vacation without an end is active.
/// </summary>
public readonly record struct VacationSpan(DateOnly Start, DateOnly? End);

/// <summary>
/// The share of days a member spent on vacation within a window of days.
/// </summary>
public readonly record struct VacationShare(int VacationDays, int WindowDays)
{
    public double Fraction => WindowDays == 0 ? 0 : (double)VacationDays / WindowDays;
}

/// <summary>
/// Questions about the vacations of a member, in local calendar dates.
/// </summary>
public static class Vacations
{
    /// <summary>
    /// Whether a vacation covers the day.
    /// </summary>
    public static bool Covers(IReadOnlyList<VacationSpan> spans, DateOnly day) =>
        spans.Any(s => s.Start <= day && (s.End is not { } end || day <= end));

    /// <summary>
    /// Whether a vacation covers at least one day from the first to the last day.
    /// </summary>
    public static bool Touches(IReadOnlyList<VacationSpan> spans, DateOnly first, DateOnly last) =>
        spans.Any(s => s.Start <= last && (s.End is not { } end || first <= end));

    /// <summary>
    /// The vacations that have ended. Only these are visible to others.
    /// </summary>
    public static IReadOnlyList<VacationSpan> Past(IReadOnlyList<VacationSpan> spans) =>
        spans.Where(s => s.End is not null).ToList();

    /// <summary>
    /// The days on vacation within the window that ends today and has the given length, or starts on
    /// the first day of the member when that lies inside the window. Only ended vacations count and each
    /// day counts once.
    /// </summary>
    public static VacationShare Share(IReadOnlyList<VacationSpan> spans, DateOnly today, int windowDays, DateOnly firstDay)
    {
        var windowStart = today.AddDays(1 - windowDays);
        if (firstDay > windowStart) windowStart = firstDay;
        if (windowStart > today) return new VacationShare(0, 0);
        var total = today.DayNumber - windowStart.DayNumber + 1;
        var days = new HashSet<int>();
        foreach (var span in Past(spans))
        {
            var from = span.Start > windowStart ? span.Start : windowStart;
            var to = span.End!.Value < today ? span.End.Value : today;
            for (var day = from.DayNumber; day <= to.DayNumber; day++) days.Add(day);
        }
        return new VacationShare(days.Count, total);
    }
}
