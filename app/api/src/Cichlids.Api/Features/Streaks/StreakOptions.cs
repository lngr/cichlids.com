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
}
