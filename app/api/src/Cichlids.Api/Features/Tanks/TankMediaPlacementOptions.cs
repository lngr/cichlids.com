namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Settings of the placement of tank media, bound from the "TankMediaPlacement" configuration section.
/// </summary>
public sealed class TankMediaPlacementOptions
{
    public const string SectionName = "TankMediaPlacement";

    /// <summary>
    /// Whether the hosted worker that places the media of tanks runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time the worker waits when no tank is due.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Time a worker holds a claimed tank. A tank whose worker does not finish within the lease is
    /// claimed by another worker.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Time a request that makes a tank non-public waits for the placement of its media before it answers.
    /// The placement continues in the background when the time is used up. Zero skips the wait.
    /// </summary>
    public TimeSpan ImmediateBudget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay before a failed placement is attempted again, multiplied by the failed attempts so far and
    /// capped at one hour.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
