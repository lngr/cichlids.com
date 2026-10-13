namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Settings of the cleanup of abandoned drafts, bound from the "DraftCleanup" configuration
/// section.
/// </summary>
public sealed class DraftCleanupOptions
{
    public const string SectionName = "DraftCleanup";

    /// <summary>
    /// Whether the hosted cleanup loop runs at all. Disabled in the shared test host, where tests
    /// run cleanup passes directly.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time between two cleanup passes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Age after which an unpublished draft is deleted with its stored objects.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Maximum number of drafts one cleanup pass deletes. A full batch is followed by the next
    /// pass at once.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}
