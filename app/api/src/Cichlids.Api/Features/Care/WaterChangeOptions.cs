namespace Cichlids.Api.Features.Care;

/// <summary>
/// Settings of the guided water change, bound from the "WaterChange" configuration section.
/// </summary>
public sealed class WaterChangeOptions
{
    public const string SectionName = "WaterChange";

    /// <summary>
    /// The time from the start of draining to the refill step (or to the finish when no refill is
    /// marked) that a guided water change needs to count as verified.
    /// </summary>
    public TimeSpan MinDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How far the EXIF capture time of an evidence photo may lie outside the session, from its start
    /// to its finish, to count for the evidence. It absorbs the clock difference between the phone
    /// and the server.
    /// </summary>
    public TimeSpan ClockTolerance { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The age after which an open session is removed with its photos.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether the hosted loop that removes expired sessions runs.
    /// </summary>
    public bool CleanupEnabled { get; set; } = true;

    /// <summary>
    /// Time between two passes of the cleanup loop.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
