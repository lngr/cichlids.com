namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// Settings of the removal of abandoned provisional accounts, bound from the
/// "ProvisionalAccountCleanup" configuration section.
/// </summary>
public sealed class ProvisionalAccountCleanupOptions
{
    public const string SectionName = "ProvisionalAccountCleanup";

    /// <summary>
    /// Whether the hosted cleanup loop runs at all. Disabled in the shared test hosts, where tests
    /// run cleanup passes directly.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time between two cleanup passes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Number of calendar months without a signed request after which a provisional account is
    /// removed with all its content.
    /// </summary>
    public int InactivityMonths { get; set; } = 12;

    /// <summary>
    /// Number of candidate accounts a cleanup pass reads from the database at once.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
