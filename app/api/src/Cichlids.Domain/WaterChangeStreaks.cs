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
/// it came in time only because a switched off rule paused the days, so the period neither breaks nor
/// extends the streak. Missed: the deadline passed before the closing water change.
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
/// Paused: a vacation, a switched off rule or a decommissioning covers today. Broken: the deadline of
/// the current period has passed.
/// </summary>
public enum WaterChangeStreakState { NoHistory, Running, Paused, Broken }

/// <summary>
/// The water change streak of a tank. Current counts the periods in a row that were verified or kept by
/// a freeze, CurrentVerified the verified ones among them, Longest the best run so far. Deadline is the
/// last day of the running period, null unless the streak is running. FreezesLeft is the allotment of
/// the tank year of today that is not used. FirstDayAfterVacation is the day after the last ended
/// vacation that belongs to the running period, null without such a vacation.
/// </summary>
public sealed record WaterChangeStreak(WaterChangeStreakState State, int Current, int CurrentVerified, int Longest,
    DateOnly? Deadline, DateOnly? LastWaterChange, int FreezesLeft, IReadOnlyList<WaterChangePeriod> Periods,
    IReadOnlyList<FreezeUse> Freezes, DateOnly? FirstDayAfterVacation = null);

/// <summary>
/// Computes the water change streak of a tank from its water changes, the history of its rhythm, the
/// vacations of the member, the decommissionings of the tank and the freezes already recorded. All dates
/// are local dates of the member; tankStart is the local date the tank was created.
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
    /// The tank year that contains a date when a tank year counts operating days: the anniversaries are
    /// those of the creation date moved forward by the decommissioned days before the date.
    /// </summary>
    public static int TankYear(DateOnly tankStart, IReadOnlyList<DecommissionSpan> decommissions, DateOnly date) =>
        TankYear(Decommissions.OperatingStart(tankStart, decommissions, date), date);

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
    /// closed by its deadline. The regular deadline is the allowed days of the rhythm that applies on
    /// each day after the water change; days of a switched off rule do not count, days of a vacation
    /// count like any other. A vacation that starts on or before the deadline and ends within the period
    /// moves the deadline to its last day plus the grace when that is later; the rule applies again to
    /// the moved deadline, and a vacation without an end leaves the period without a deadline. A period that
    /// a decommissioning interrupts while it is in time runs from the day the tank is put back into
    /// operation, so the decommissioned days do not count; a period that was already missed stays missed.
    /// A water change on a decommissioned day is ignored, and a decommissioned tank has a paused streak. A closing
    /// water change that came in time counts when it is verified, or when a freeze keeps the period: a
    /// recorded freeze first, otherwise a new one while the allotment of the tank year of the period end
    /// is not used up (recorded freezes count against it first). The tank year counts operating days. A period that only a switched off rule
    /// brings in time does not change the length. After a break the closing water change starts a new
    /// run. Water changes after today are ignored.
    /// </summary>
    public static WaterChangeStreak Evaluate(IReadOnlyList<WaterChangeRecord> changes, IReadOnlyList<RhythmChange> rhythm,
        IReadOnlyList<VacationSpan> vacations, IReadOnlySet<long> recordedFreezes, int freezesPerYear, DateOnly tankStart, DateOnly today, int utcOffsetMinutes,
        IReadOnlyList<DecommissionSpan>? decommissions = null)
    {
        decommissions ??= [];
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
        int YearOf(DateOnly day) => TankYear(tankStart, decommissions, day);
        // The last day on which a period that began on the anchor day is in time, judged on the days up
        // to the given day. Vacations are not considered here.
        DateOnly RegularDeadline(DateOnly anchor, DateOnly upTo, bool honorRulePauses)
        {
            var elapsed = 0;
            for (var day = anchor.AddDays(1); day <= upTo; day = day.AddDays(1))
            {
                if (honorRulePauses && !RhythmAt(day).Enabled) continue;
                elapsed++;
                if (elapsed > AllowedDays(RhythmAt(day).RhythmDays)) return day.AddDays(-1);
            }
            var extrapolated = upTo.AddDays(AllowedDays(RhythmAt(upTo).RhythmDays) - elapsed);
            return extrapolated > upTo ? extrapolated : upTo;
        }
        // The regular deadline moved by the vacations of the period; null while a vacation without an end
        // reaches the deadline.
        DateOnly? DeadlineFrom(DateOnly anchor, DateOnly upTo)
        {
            var deadline = RegularDeadline(anchor, upTo, honorRulePauses: true);
            for (var moved = true; moved;)
            {
                moved = false;
                foreach (var vacation in vacations)
                {
                    if (vacation.Start > deadline || vacation.End <= anchor) continue;
                    if (vacation.End is not { } end) return null;
                    var candidate = end.AddDays(GraceDays(RhythmAt(end).RhythmDays));
                    if (candidate <= deadline) continue;
                    deadline = candidate;
                    moved = true;
                }
            }
            return deadline;
        }
        // The day a period that began on the anchor day runs from, judged on the days up to the given day:
        // the end day of the last decommissioning in between that found the period still in time on the day
        // before it began, otherwise the anchor day.
        DateOnly Restart(DateOnly anchor, DateOnly upTo)
        {
            var restart = anchor;
            foreach (var span in decommissions.OrderBy(s => s.Start))
            {
                if (span.End is not { } end || end <= restart || end > upTo) continue;
                var eve = span.Start.AddDays(-1);
                if (eve > restart && DeadlineFrom(restart, eve) is { } limit && limit < eve) break;
                restart = end;
            }
            return restart;
        }
        DateOnly? Deadline(DateOnly anchor, DateOnly upTo) => DeadlineFrom(Restart(anchor, upTo), upTo);
        bool Missed(DateOnly anchor, DateOnly closing) => Deadline(anchor, closing) is { } limit && closing > limit;
        bool Bridged(DateOnly anchor, DateOnly closing)
        {
            var from = Restart(anchor, closing);
            return closing > RegularDeadline(from, closing, honorRulePauses: false) && closing <= RegularDeadline(from, closing, honorRulePauses: true);
        }

        var days = changes.Select(c => (c.Id, Date: CarePlans.LocalDate(c.OccurredAt, utcOffsetMinutes), c.Verified))
            .Where(c => c.Date <= today && !Decommissions.Covers(decommissions, c.Date))
            .GroupBy(c => c.Date).OrderBy(g => g.Key)
            .Select(g => new Day(g.Key, g.OrderBy(c => c.Id).Select(c => c.Id).ToList(), g.Any(c => c.Verified))).ToList();

        var used = new Dictionary<int, int>();
        foreach (var day in days.Where(d => d.Ids.Any(recordedFreezes.Contains)))
        {
            var tankYear = YearOf(day.Date);
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
            if (Missed(anchor.Date, closing.Date)) outcome = PeriodOutcome.Missed;
            else if (Bridged(anchor.Date, closing.Date)) outcome = PeriodOutcome.Bridged;
            else if (closing.Verified) outcome = PeriodOutcome.Verified;
            else
            {
                var year = YearOf(closing.Date);
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

        var freezesLeft = Math.Max(0, freezesPerYear - used.GetValueOrDefault(YearOf(today)));
        if (days.Count == 0)
            return new WaterChangeStreak(WaterChangeStreakState.NoHistory, 0, 0, 0, null, null, freezesLeft, periods, freezes);

        var last = days[^1].Date;
        if (decommissions.Where(d => d.Start <= today && (d.End is not { } end || today < end)).Select(d => (DateOnly?)d.Start).Min() is { } began)
        {
            var eve = began.AddDays(-1);
            return Deadline(last, eve) is { } limit && limit < eve
                ? new WaterChangeStreak(WaterChangeStreakState.Broken, 0, 0, longest, null, last, freezesLeft, periods, freezes)
                : new WaterChangeStreak(WaterChangeStreakState.Paused, current, currentVerified, longest, null, last, freezesLeft, periods, freezes);
        }
        var periodDeadline = Deadline(last, today);
        if (periodDeadline is { } due && today > due)
            return new WaterChangeStreak(WaterChangeStreakState.Broken, 0, 0, longest, null, last, freezesLeft, periods, freezes);
        if (periodDeadline is not { } runningDeadline || IsPaused(today))
            return new WaterChangeStreak(WaterChangeStreakState.Paused, current, currentVerified, longest, null, last, freezesLeft, periods, freezes);
        var ended = vacations.Where(v => v.End is { } end && end > last && end < today && v.Start <= runningDeadline).Select(v => v.End!.Value).ToList();
        DateOnly? firstDayAfterVacation = ended.Count == 0 ? null : ended.Max().AddDays(1);
        return new WaterChangeStreak(WaterChangeStreakState.Running, current, currentVerified, longest, runningDeadline, last, freezesLeft, periods, freezes, firstDayAfterVacation);
    }
}
