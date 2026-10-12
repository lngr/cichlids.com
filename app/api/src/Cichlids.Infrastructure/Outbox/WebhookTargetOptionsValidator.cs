using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Rejects a webhook target allowlist that contains anything other than bare host names or IP
/// addresses, so a mistyped entry such as a full URL fails the startup of the host.
/// </summary>
public sealed class WebhookTargetOptionsValidator : IValidateOptions<WebhookTargetOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookTargetOptions options)
    {
        var invalid = options.AllowedTargetHosts.Where(entry => !WebhookTargetPolicy.IsBareHostName(entry)).ToList();

        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{WebhookTargetOptions.SectionName}:AllowedTargetHosts accepts only bare host names or IP addresses; "
                + $"invalid entries: {string.Join(", ", invalid.Select(entry => $"'{entry}'"))}.");
    }
}
