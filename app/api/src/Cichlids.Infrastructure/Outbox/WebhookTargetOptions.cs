using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Binds the "Webhooks" configuration section and validates it. A missing section yields an
    /// empty allowlist. An entry that is not a bare host name or IP address raises an
    /// OptionsValidationException naming the invalid entries.
    /// </summary>
    public static WebhookTargetOptions Resolve(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<WebhookTargetOptions>() ?? new WebhookTargetOptions();

        var result = new WebhookTargetOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(WebhookTargetOptions), result.Failures);
        }

        return options;
    }
}
