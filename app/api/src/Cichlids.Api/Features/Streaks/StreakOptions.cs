namespace Cichlids.Api.Features.Streaks;

/// <summary>
/// Settings of the streaks, bound from the "Streaks" configuration section.
/// </summary>
public sealed class StreakOptions
{
    public const string SectionName = "Streaks";

    /// <summary>
    /// The freezes a tank has per calendar year. A freeze keeps a period of the water change streak
    /// whose water change has no verified evidence.
    /// </summary>
    public int FreezesPerYear { get; set; } = Cichlids.Domain.WaterChangeStreaks.DefaultFreezesPerYear;

    /// <summary>
    /// How many days the local date a client sends may differ from the date of the server in UTC. The
    /// offsets of the world span 26 hours, so one day covers every honest clock.
    /// </summary>
    public int MaxClientDateSkewDays { get; set; } = 1;

    /// <summary>
    /// The longest vacation in days. A longer vacation is refused, and an active vacation that has run
    /// longer pauses the streaks only for this many days.
    /// </summary>
    public int MaxVacationDays { get; set; } = 366;

    /// <summary>
    /// Vacations that start at most this many days before today may pause a period that has already
    /// ended; a vacation that starts earlier is refused when it would repair a broken period or week.
    /// </summary>
    public int RepairWindowDays { get; set; } = 7;
}
