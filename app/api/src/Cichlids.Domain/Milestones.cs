using System.Globalization;

namespace Cichlids.Domain;

/// <summary>
/// The kinds of milestones a member is congratulated on. FirstCareWeek: the first time a tank met its
/// water change plan. KeptStreak: a time-lapse streak of 4, 12, 26 or 52 weeks. TankAnniversary: a
/// full tank year, counted in operating days. FishAnniversary: a full year since a named fish moved in.
/// </summary>
public enum MilestoneKind { FirstCareWeek, KeptStreak, TankAnniversary, FishAnniversary }

/// <summary>
/// Rules of the milestones on Today. A milestone has a stable key made of its kind, its subject (the
/// tank, or the named fish of a fish anniversary) and the threshold in weeks or the number of the full
/// year, so the same achievement has one key for ever. It shows from the day it is reached for a fixed
/// number of days.
/// </summary>
public static class Milestones
{
    /// <summary>
    /// The streak lengths in weeks that count as a milestone.
    /// </summary>
    public static readonly IReadOnlyList<int> StreakWeeks = [4, 12, 26, 52];

    /// <summary>
    /// The number of days a milestone shows, counting the day it is reached.
    /// </summary>
    public const int VisibleDays = 7;

    /// <summary>
    /// The highest year of a fish anniversary.
    /// </summary>
    public const int MaxFishYear = 100;

    private const string FirstCareWeekName = "first_care_week";
    private const string KeptStreakName = "kept_streak";
    private const string TankAnniversaryName = "tank_anniversary";
    private const string FishAnniversaryName = "fish_anniversary";

    /// <summary>
    /// Whether a milestone reached on a day shows on the given day.
    /// </summary>
    public static bool IsVisible(DateOnly reachedOn, DateOnly today) =>
        reachedOn <= today && today.DayNumber - reachedOn.DayNumber < VisibleDays;

    /// <summary>
    /// The key of a milestone. The subject is the tank, or the named fish of a fish anniversary. The value
    /// is the threshold in weeks of a kept streak, the number of the full year of an anniversary and unused
    /// for the first care week.
    /// </summary>
    public static string Key(MilestoneKind kind, long subjectId, int value) => kind switch
    {
        MilestoneKind.FirstCareWeek => $"{FirstCareWeekName}:{subjectId.ToString(CultureInfo.InvariantCulture)}",
        MilestoneKind.KeptStreak => $"{KeptStreakName}:{subjectId.ToString(CultureInfo.InvariantCulture)}:{value.ToString(CultureInfo.InvariantCulture)}",
        MilestoneKind.TankAnniversary => $"{TankAnniversaryName}:{subjectId.ToString(CultureInfo.InvariantCulture)}:{value.ToString(CultureInfo.InvariantCulture)}",
        MilestoneKind.FishAnniversary => $"{FishAnniversaryName}:{subjectId.ToString(CultureInfo.InvariantCulture)}:{value.ToString(CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The parts of a key, or null when the text is no key of a milestone: an unknown kind, a subject that
    /// is not a positive number, a streak length outside the thresholds, a tank year below 1 or a fish
    /// year outside 1 to 100.
    /// </summary>
    public static (MilestoneKind Kind, long SubjectId, int Value)? TryParseKey(string? key)
    {
        var parts = key?.Split(':');
        if (parts is null || parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var subjectId) || subjectId < 1)
            return null;
        if (parts.Length == 2 && parts[0] == FirstCareWeekName) return (MilestoneKind.FirstCareWeek, subjectId, 0);
        if (parts.Length != 3 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return null;
        return parts[0] switch
        {
            KeptStreakName when StreakWeeks.Contains(value) => (MilestoneKind.KeptStreak, subjectId, value),
            TankAnniversaryName when value >= 1 => (MilestoneKind.TankAnniversary, subjectId, value),
            FishAnniversaryName when value is >= 1 and <= MaxFishYear => (MilestoneKind.FishAnniversary, subjectId, value),
            _ => null,
        };
    }

    /// <summary>
    /// The full tank years completed on the days from the first to the last day, each with the day it
    /// is completed: the day on which the tank year counted in operating days advances.
    /// </summary>
    public static IReadOnlyList<(int Year, DateOnly Date)> Anniversaries(
        DateOnly tankStart, IReadOnlyList<DecommissionSpan> decommissions, DateOnly first, DateOnly last)
    {
        var result = new List<(int, DateOnly)>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var year = WaterChangeStreaks.TankYear(tankStart, decommissions, day);
            if (year > 1 && year > WaterChangeStreaks.TankYear(tankStart, decommissions, day.AddDays(-1))) result.Add((year - 1, day));
        }
        return result;
    }

    /// <summary>
    /// The day a named fish completes the given year: the same calendar day that many years after it moved
    /// in. A day the year lacks (29 February) falls on the last day of the month.
    /// </summary>
    public static DateOnly FishAnniversaryDate(DateOnly movedInOn, int year) => movedInOn.AddYears(year);

    /// <summary>
    /// The full years a named fish has spent with its keeper on the given day, at most the highest fish year.
    /// </summary>
    public static int CompletedFishYears(DateOnly movedInOn, DateOnly day)
    {
        var years = Math.Min(day.Year - movedInOn.Year, MaxFishYear);
        if (years < 1) return 0;
        return FishAnniversaryDate(movedInOn, years) > day ? years - 1 : years;
    }

    /// <summary>
    /// The full years of a named fish that are completed on the days from the first to the last day, each
    /// with the day it is completed.
    /// </summary>
    public static IReadOnlyList<(int Year, DateOnly Date)> FishAnniversaries(DateOnly movedInOn, DateOnly first, DateOnly last)
    {
        var result = new List<(int, DateOnly)>();
        for (var year = Math.Max(1, first.Year - movedInOn.Year); year <= Math.Min(MaxFishYear, last.Year - movedInOn.Year); year++)
        {
            var date = FishAnniversaryDate(movedInOn, year);
            if (date >= first && date <= last) result.Add((year, date));
        }
        return result;
    }

    /// <summary>
    /// The day the water change plan of a tank was met for the first time: the closing day of the first
    /// period that a verified water change or a freeze kept. Null while no period was kept.
    /// </summary>
    public static DateOnly? FirstCareWeek(IReadOnlyList<WaterChangePeriod> periods) =>
        periods.Where(p => p.Outcome is PeriodOutcome.Verified or PeriodOutcome.Frozen).Select(p => (DateOnly?)p.End).Min();
}
