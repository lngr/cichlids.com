namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Configuration for the hosts webhooks may be delivered to, bound from the "Webhooks"
/// configuration section. The list is part of each environment's configuration in Git, so every
/// new target host goes through review. An empty list allows no target at all.
/// </summary>
public class WebhookTargetOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>
    /// Bare host names or IP addresses, matched exactly and case-insensitively against the host of
    /// a webhook URL. Entries carry no scheme, port, path or wildcard.
    /// </summary>
    public List<string> AllowedTargetHosts { get; set; } = [];
}
