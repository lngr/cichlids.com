namespace Cichlids.Domain;

/// <summary>
/// A water change as the streak sees it: the care entry, when it occurred and whether the guided mode
/// verified it.
/// </summary>
public sealed record WaterChangeRecord(long Id, DateTimeOffset OccurredAt, bool Verified);

/// <summary>
/// The water change rule of a tank from the given instant on: its rhythm in days and whether it is
/// switched on.
/// </summary>
public sealed record RhythmChange(DateTimeOffset At, int RhythmDays, bool Enabled);

/// <summary>
/// How a period between two water changes ended. Verified: the closing water change came in time and
/// is verified. Frozen: it came in time without verification and a freeze kept the period. Bridged:
/// it came in time only because a vacation or a switched off rule paused the days, so the period
/// neither breaks nor extends the streak. Missed: the deadline passed before the closing water change.
/// Unverified: it came in time without verification and no freeze was left.
/// </summary>
public enum PeriodOutcome { Verified, Frozen, Bridged, Missed, Unverified }

/// <summary>
/// One period between two water changes, from the date of the first to the date of the closing one.
/// </summary>
public sealed record WaterChangePeriod(long WaterChangeId, DateOnly Start, DateOnly End, PeriodOutcome Outcome);

/// <summary>
/// A freeze that kept a period: the closing water change, the tank year of the period end that the
/// freeze is taken from (1 is the first tank year) and whether the usage was recorded before this evaluation.
/// </summary>
public sealed record FreezeUse(long WaterChangeId, int TankYear, DateOnly PeriodStart, DateOnly PeriodEnd, bool Recorded);

/// <summary>
/// NoHistory: the tank has no water change. Running: the current period is within its deadline.
/// Paused: a vacation or a switched off rule covers today. Broken: the deadline of the current period
/// has passed.
/// </summary>
public enum WaterChangeStreakState { NoHistory, Running, Paused, Broken }

/// <summary>
/// The water change streak of a tank. Current counts the periods in a row that were verified or kept by
/// a freeze, CurrentVerified the verified ones among them, Longest the best run so far. Deadline is the
/// last day of the running period, null unless the streak is running. FreezesLeft is the allotment of
/// the tank year of today that is not used.
/// </summary>
public sealed record WaterChangeStreak(WaterChangeStreakState State, int Current, int CurrentVerified, int Longest,
    DateOnly? Deadline, DateOnly? LastWaterChange, int FreezesLeft, IReadOnlyList<WaterChangePeriod> Periods,
    IReadOnlyList<FreezeUse> Freezes);

/// <summary>
/// Computes the water change streak of a tank from its water changes, the history of its rhythm, the
/// vacations of the member and the freezes already recorded. All dates are local dates of the member;
/// tankStart is the local date the tank was created.
/// </summary>
public static class WaterChangeStreaks
{
    public const int DefaultFreezesPerYear = 2;

    /// <summary>
    /// The tank year that contains a date, counted from 1. The first tank year starts on the creation
    /// date of the tank and every further one on its calendar anniversary, so a tank year has 365 or 366
    /// days; a tank created on February 29 has its anniversary on February 28 in years without that day.
    /// Dates before the creation date belong to the first tank year.
    /// </summary>
    public static int TankYear(DateOnly tankStart, DateOnly date)
    {
        var years = date.Year - tankStart.Year;
        if (years <= 0) return 1;
        if (date < tankStart.AddYears(years)) years--;
        return years + 1;
    }

    /// <summary>
    /// The grace of a rhythm in days: one third of the rhythm, rounded up.
    /// </summary>
    public static int GraceDays(int rhythmDays) => (rhythmDays + 2) / 3;

    /// <summary>
    /// The number of days after a water change by which the next one has to come: the rhythm plus
    /// the grace.
    /// </summary>
    public static int AllowedDays(int rhythmDays) => rhythmDays + GraceDays(rhythmDays);

    /// <summary>
    /// The first day on which the member is reminded of a running streak: the last day of the rhythm
    /// before the grace begins, which is the grace in days before the deadline. The reminders of the
    /// period fall on every day from there through the deadline.
    /// </summary>
    public static DateOnly FirstReminderDay(DateOnly deadline, int rhythmDays) => deadline.AddDays(-GraceDays(rhythmDays));

    private sealed record Day(DateOnly Date, IReadOnlyList<long> Ids, bool Verified);

    /// <summary>
    /// Walks the water changes in order. A water change on the same local day as another counts as
    /// one, verified when any of them is. Each water change starts the next period, which has to be
    /// closed within the allowed days of the rhythm that applies on each day; days of a vacation or of
    /// a switched off rule do not count. A closing water change that came in time counts when it is
    /// verified, or when a freeze keeps the period: a recorded freeze first, otherwise a new one while
    /// the allotment of the tank year of the period end is not used up (recorded freezes count
    /// against it first). A period that vacation alone bridges does not change the length. After a
    /// break the closing water change starts a new run. Water changes after today are ignored.
    /// </summary>
    public static WaterChangeStreak Evaluate(IReadOnlyList<WaterChangeRecord> changes, IReadOnlyList<RhythmChange> rhythm,
        IReadOnlyList<VacationSpan> vacations, IReadOnlySet<long> recordedFreezes, int freezesPerYear, DateOnly tankStart, DateOnly today, int utcOffsetMinutes)
    {
        var schedule = rhythm.Select(r => (From: CarePlans.LocalDate(r.At, utcOffsetMinutes), r.RhythmDays, r.Enabled))
            .OrderBy(r => r.From).ToList();
        (int RhythmDays, bool Enabled) RhythmAt(DateOnly day)
        {
            var found = (RhythmDays: CarePlans.DefaultWaterChangeRhythmDays, Enabled: true);
            foreach (var entry in schedule)
            {
                if (entry.From > day) break;
                found = (entry.RhythmDays, entry.Enabled);
            }
            return found;
        }
        bool IsPaused(DateOnly day) => !RhythmAt(day).Enabled || Vacations.Covers(vacations, day);
        bool Missed(DateOnly anchor, DateOnly end, bool honorPauses)
        {
            var elapsed = 0;
            for (var day = anchor.AddDays(1); day <= end; day = day.AddDays(1))
            {
                if (honorPauses && IsPaused(day)) continue;
                elapsed++;
                if (elapsed > AllowedDays(RhythmAt(day).RhythmDays)) return true;
            }
            return false;
        }

        var days = changes.Select(c => (c.Id, Date: CarePlans.LocalDate(c.OccurredAt, utcOffsetMinutes), c.Verified))
            .Where(c => c.Date <= today)
            .GroupBy(c => c.Date).OrderBy(g => g.Key)
            .Select(g => new Day(g.Key, g.OrderBy(c => c.Id).Select(c => c.Id).ToList(), g.Any(c => c.Verified))).ToList();

        var used = new Dictionary<int, int>();
        foreach (var day in days.Where(d => d.Ids.Any(recordedFreezes.Contains)))
        {
            var tankYear = TankYear(tankStart, day.Date);
            used[tankYear] = used.GetValueOrDefault(tankYear) + 1;
        }

        var periods = new List<WaterChangePeriod>();
        var freezes = new List<FreezeUse>();
        int current = 0, currentVerified = 0, longest = 0;
        for (var i = 1; i < days.Count; i++)
        {
            Day anchor = days[i - 1], closing = days[i];
            var id = closing.Ids[0];
            PeriodOutcome outcome;
            if (Missed(anchor.Date, closing.Date, honorPauses: true)) outcome = PeriodOutcome.Missed;
            else if (Missed(anchor.Date, closing.Date, honorPauses: false)) outcome = PeriodOutcome.Bridged;
            else if (closing.Verified) outcome = PeriodOutcome.Verified;
            else
            {
                var year = TankYear(tankStart, closing.Date);
                var recorded = closing.Ids.Where(recordedFreezes.Contains).Cast<long?>().FirstOrDefault();
                if (recorded is { } recordedId)
                {
                    freezes.Add(new FreezeUse(recordedId, year, anchor.Date, closing.Date, true));
                    outcome = PeriodOutcome.Frozen;
                }
                else if (used.GetValueOrDefault(year) < freezesPerYear)
                {
                    used[year] = used.GetValueOrDefault(year) + 1;
                    freezes.Add(new FreezeUse(id, year, anchor.Date, closing.Date, false));
                    outcome = PeriodOutcome.Frozen;
                }
                else outcome = PeriodOutcome.Unverified;
            }
            periods.Add(new WaterChangePeriod(id, anchor.Date, closing.Date, outcome));
            switch (outcome)
            {
                case PeriodOutcome.Verified:
                    current++;
                    currentVerified++;
                    break;
                case PeriodOutcome.Frozen:
                    current++;
                    break;
                case PeriodOutcome.Missed or PeriodOutcome.Unverified:
                    current = 0;
                    currentVerified = 0;
                    break;
            }
            longest = Math.Max(longest, current);
        }

        var freezesLeft = Math.Max(0, freezesPerYear - used.GetValueOrDefault(TankYear(tankStart, today)));
        if (days.Count == 0)
            return new WaterChangeStreak(WaterChangeStreakState.NoHistory, 0, 0, 0, null, null, freezesLeft, periods, freezes);

        var last = days[^1].Date;
        if (Missed(last, today, honorPauses: true))
            return new WaterChangeStreak(WaterChangeStreakState.Broken, 0, 0, longest, null, last, freezesLeft, periods, freezes);
        if (IsPaused(today))
            return new WaterChangeStreak(WaterChangeStreakState.Paused, current, currentVerified, longest, null, last, freezesLeft, periods, freezes);
        var elapsedToday = 0;
        for (var day = last.AddDays(1); day <= today; day = day.AddDays(1)) if (!IsPaused(day)) elapsedToday++;
        var deadline = today.AddDays(AllowedDays(RhythmAt(today).RhythmDays) - elapsedToday);
        return new WaterChangeStreak(WaterChangeStreakState.Running, current, currentVerified, longest, deadline, last, freezesLeft, periods, freezes);
    }
}
