namespace Cichlids.Domain;

/// <summary>
/// A photo of the time-lapse series that was taken with the camera of the app, with the instant the
/// server received it.
/// </summary>
public sealed record SeriesPhoto(long MediaItemId, DateTimeOffset At);

/// <summary>
/// The time-lapse streak of a tank. Current is the number of ISO weeks in a row with a photo (a week
/// that vacation bridges neither breaks nor extends it), Longest the best run so far. ThisWeekOpen
/// tells whether a photo this week would still count, Paused whether a vacation covers today.
/// KeepingPhotoIds lists the photos that are the only photo of a week of the current run, so deleting
/// one breaks the streak.
/// </summary>
public sealed record TimeLapseStreak(int Current, int Longest, bool ThisWeekHasPhoto, bool ThisWeekOpen, bool Paused,
    DateOnly WeekStart, DateOnly WeekEnd, IReadOnlyList<long> KeepingPhotoIds);

/// <summary>
/// Computes the time-lapse streak from the in-app photos of a tank. A week runs from Monday to Sunday
/// in the local dates of the member. A week without a photo breaks the run unless a vacation touches
/// it; a week with a photo counts whether or not a vacation touches it.
/// </summary>
public static class TimeLapseStreaks
{
    /// <summary>
    /// The Monday of the ISO week of a date.
    /// </summary>
    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static TimeLapseStreak Evaluate(IReadOnlyList<SeriesPhoto> photos, int utcOffsetMinutes,
        IReadOnlyList<VacationSpan> vacations, DateOnly today)
    {
        var thisWeek = WeekStartOf(today);
        var byWeek = photos.Select(p => (p.MediaItemId, Date: CarePlans.LocalDate(p.At, utcOffsetMinutes)))
            .Where(p => p.Date <= today)
            .GroupBy(p => WeekStartOf(p.Date))
            .ToDictionary(g => g.Key, g => g.Select(p => p.MediaItemId).Order().ToList());
        var paused = Vacations.Covers(vacations, today);
        var hasPhoto = byWeek.ContainsKey(thisWeek);
        var current = 0;
        var longest = 0;
        List<long> keeping = [];
        if (byWeek.Count > 0)
        {
            for (var week = byWeek.Keys.Min(); week <= thisWeek; week = week.AddDays(7))
            {
                if (byWeek.TryGetValue(week, out var ids))
                {
                    current++;
                    longest = Math.Max(longest, current);
                    if (ids.Count == 1) keeping.Add(ids[0]);
                }
                else if (week != thisWeek && !Vacations.Touches(vacations, week, week.AddDays(6)))
                {
                    current = 0;
                    keeping = [];
                }
            }
        }
        return new TimeLapseStreak(current, longest, hasPhoto, !hasPhoto && !paused, paused, thisWeek, thisWeek.AddDays(6), keeping);
    }
}
