namespace Cichlids.Domain.Entities;

/// <summary>
/// A registered consumer of outbox events, delivered as signed webhook calls.
/// </summary>
public class WebhookSubscription
{
    public long Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public List<string> EventTypes { get; set; } = [];
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
