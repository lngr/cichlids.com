namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Configuration for outbox dispatch, bound from the "OutboxDispatcher" configuration section.
/// </summary>
public class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// Whether the hosted polling loop runs at all. Disabled in the shared test host, where
    /// individual tests drive dispatch passes directly instead of racing a timer.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of outbox rows one dispatch pass locks and considers.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Number of failed delivery attempts after which an event is dropped as a dead letter
    /// instead of being retried again.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Ceiling on the exponential redelivery backoff, so a long-stuck subscriber cannot push the
    /// wait between attempts arbitrarily high.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(1);
}
