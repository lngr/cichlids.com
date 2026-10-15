using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// The reason codes of a care rule. A reason is a code plus optional parameters, so reasons that
/// depend on the stocking fit the same shape.
/// </summary>
public static class CareReasons
{
    public const string CichlidDefault = "cichlid_default";
    public const string MemberChoice = "member_choice";
}

/// <summary>
/// One rule of a care plan: how often a kind of care is due, whether the rule is switched on, and
/// why it has this rhythm.
/// </summary>
public sealed record CareRule(CareKind Kind, int RhythmDays, bool Enabled, string ReasonCode,
    IReadOnlyDictionary<string, string>? ReasonParameters);

/// <summary>
/// The due state of an enabled rule on a local date. Days is the number of days until the due
/// date (due in) or since it (overdue), and 0 for due today and never logged. The due date is
/// null when the tank has no entry of the kind.
/// </summary>
public readonly record struct CareDue(CareDueState State, int Days, DateOnly? DueDate);

/// <summary>
/// The default care plan and the derivation of due dates from the newest entry of a kind.
/// </summary>
public static class CarePlans
{
    public const int MinRhythmDays = 1;
    public const int MaxRhythmDays = 90;
    public const int DefaultWaterChangeRhythmDays = 7;
    private const int MaxUtcOffsetMinutes = 14 * 60;

    /// <summary>
    /// The rule that applies when a tank has no stored rule of the kind: a water change every
    /// seven days; a measurement has none.
    /// </summary>
    public static CareRule? DefaultFor(CareKind kind) => kind == CareKind.WaterChange
        ? new CareRule(kind, DefaultWaterChangeRhythmDays, true, CareReasons.CichlidDefault, null)
        : null;

    public static bool IsValidRhythm(int days) => days is >= MinRhythmDays and <= MaxRhythmDays;

    public static bool IsValidUtcOffset(int minutes) => minutes is >= -MaxUtcOffsetMinutes and <= MaxUtcOffsetMinutes;

    /// <summary>
    /// The calendar date of an instant for a member whose clock is the given number of minutes
    /// ahead of UTC.
    /// </summary>
    public static DateOnly LocalDate(DateTimeOffset instant, int utcOffsetMinutes) =>
        DateOnly.FromDateTime(instant.UtcDateTime.AddMinutes(utcOffsetMinutes));

    /// <summary>
    /// The due state of a rule on the member's local date, derived from the occurred time of the
    /// newest entry of the kind. A disabled rule has no due state (null). Without any entry the
    /// state is never logged, so a fresh tank carries no overdue pressure.
    /// </summary>
    public static CareDue? DueFor(CareRule rule, DateTimeOffset? lastOccurredAt, DateOnly today, int utcOffsetMinutes)
    {
        if (!rule.Enabled) return null;
        if (lastOccurredAt is not { } last) return new CareDue(CareDueState.NeverLogged, 0, null);
        var due = LocalDate(last, utcOffsetMinutes).AddDays(rule.RhythmDays);
        var difference = due.DayNumber - today.DayNumber;
        return difference switch
        {
            > 0 => new CareDue(CareDueState.DueIn, difference, due),
            0 => new CareDue(CareDueState.DueToday, 0, due),
            _ => new CareDue(CareDueState.Overdue, -difference, due),
        };
    }
}
