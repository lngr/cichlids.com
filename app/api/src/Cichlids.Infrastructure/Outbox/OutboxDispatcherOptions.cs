namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Configuration for outbox dispatch, bound from the "OutboxDispatcher" configuration section.
/// </summary>
public class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// Whether the hosted polling loop runs at all. Disabled in the shared test host, where
    /// individual tests run dispatch passes directly to avoid racing a timer.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of due outbox events one dispatch pass reserves.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Ceiling on the exponential redelivery backoff, so a long-stuck subscriber cannot push the
    /// wait between attempts arbitrarily high.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Period after the start of an event's delivery window at which a failing event is given up.
    /// </summary>
    public TimeSpan GiveUpAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a dispatcher instance holds the events it reserved. Events whose result is not
    /// recorded within the lease become due again for any instance.
    /// </summary>
    public TimeSpan ReservationLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Time limit for one webhook request, from sending it until the response headers arrive.
    /// </summary>
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
