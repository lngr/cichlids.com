namespace Cichlids.Api.Features.Media;

/// <summary>
/// Settings of the placement of media, bound from the "MediaPlacement" configuration section.
/// </summary>
public sealed class MediaPlacementOptions
{
    public const string SectionName = "MediaPlacement";

    /// <summary>
    /// Whether the hosted worker that places media runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time the worker waits when no media item is due.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Time a worker holds a claimed media item. An item whose worker does not finish within the lease is
    /// claimed by another worker.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Time a request that changes who sees media waits for the placement of the affected items before it
    /// answers. The placement continues in the background when the time is used up. Zero skips the wait.
    /// </summary>
    public TimeSpan ImmediateBudget { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay before a failed placement is attempted again, multiplied by the failed attempts so far and
    /// capped at one hour.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Time between two checks that compare the placement of every media item with the visibility rule and
    /// mark the items that differ.
    /// </summary>
    public TimeSpan ReconcileInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The most media items one check marks.
    /// </summary>
    public int ReconcileBatchSize { get; set; } = 500;
}
