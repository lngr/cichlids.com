using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// The validated Web Push configuration from the section "WebPush". Web Push is off unless
/// WebPush:Enabled is true. An enabled deployment outside Development needs the VAPID key pair
/// (WebPush:VapidPublicKey and WebPush:VapidPrivateKey, or a JSON key file named by
/// WebPush:VapidKeyFile with the members publicKey and privateKey) and a contact subject
/// (WebPush:Subject, a mailto or https address without personal data), and refuses to start
/// without them. Development without keys leaves Web Push unavailable. The allowlist
/// WebPush:AllowedPushHosts names the push services a subscription may point to; an entry matches
/// the host itself and every subdomain of it.
/// </summary>
public sealed partial class WebPushSettings
{
    /// <summary>The push services of the major browsers, used when no allowlist is configured.</summary>
    public static readonly IReadOnlyList<string> DefaultPushHosts =
    [
        "fcm.googleapis.com",
        "push.services.mozilla.com",
        "push.apple.com",
        "notify.windows.com",
    ];

    /// <summary>The upper bound of subscriptions one member keeps.</summary>
    public const int DefaultMaxSubscriptionsPerMember = 10;

    private const string Section = "WebPush";

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BareHost();

    private WebPushSettings(bool enabled, string? publicKey, string? privateKey, string? subject,
        IReadOnlyList<string> allowedPushHosts, int maxSubscriptionsPerMember)
    {
        Enabled = enabled;
        PublicKey = publicKey;
        PrivateKey = privateKey;
        Subject = subject;
        AllowedPushHosts = allowedPushHosts;
        MaxSubscriptionsPerMember = maxSubscriptionsPerMember;
    }

    /// <summary>Whether Web Push is switched on in the configuration.</summary>
    public bool Enabled { get; }

    /// <summary>The VAPID public key as base64url, or null without keys.</summary>
    public string? PublicKey { get; }

    /// <summary>The VAPID private key as base64url, or null without keys.</summary>
    public string? PrivateKey { get; }

    /// <summary>The contact address of the application server, or null without keys.</summary>
    public string? Subject { get; }

    /// <summary>The push service hosts a subscription endpoint may use.</summary>
    public IReadOnlyList<string> AllowedPushHosts { get; }

    /// <summary>The most subscriptions one member keeps.</summary>
    public int MaxSubscriptionsPerMember { get; }

    /// <summary>Whether subscriptions are accepted and reminders are sent: enabled and with a key pair.</summary>
    public bool Available => Enabled && PublicKey is not null;

    /// <summary>
    /// Reads and validates the configuration for the given host environment name and throws an
    /// exception naming the configuration key when it is invalid or, outside Development, keys are missing.
    /// </summary>
    public static WebPushSettings Resolve(IConfiguration configuration, string? environmentName, string contentRootPath)
    {
        var section = configuration.GetSection(Section);
        var configured = section.GetSection("AllowedPushHosts").GetChildren().Select(c => c.Value ?? string.Empty).ToList();
        foreach (var entry in configured.Where(entry => !BareHost().IsMatch(entry)))
        {
            throw Refuse(environmentName, $"{Section}:AllowedPushHosts holds the entry '{entry}', which is not a bare host name.");
        }

        var hosts = configured.Count > 0 ? configured.Select(h => h.ToLowerInvariant()).ToList() : [.. DefaultPushHosts];
        var max = section.GetValue<int?>("MaxSubscriptionsPerMember") ?? DefaultMaxSubscriptionsPerMember;
        if (max < 1)
        {
            throw Refuse(environmentName, $"{Section}:MaxSubscriptionsPerMember must be at least 1.");
        }

        if (!section.GetValue<bool>("Enabled"))
        {
            return new WebPushSettings(false, null, null, null, hosts, max);
        }

        var (publicKey, privateKey) = ReadKeys(section, environmentName, contentRootPath);
        if (publicKey is null || privateKey is null)
        {
            if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                throw Refuse(environmentName, $"{Section}:Enabled is true, but {Section}:VapidPublicKey and {Section}:VapidPrivateKey are not set. Generate a key pair with tools/create-vapid-keys.cs.");
            }

            return new WebPushSettings(true, null, null, null, hosts, max);
        }

        if (!VapidKeys.IsPair(publicKey, privateKey))
        {
            throw Refuse(environmentName, $"{Section}:VapidPrivateKey is not the private key of {Section}:VapidPublicKey. Both are base64url keys of a P-256 pair.");
        }

        var subject = section["Subject"];
        if (!IsContactSubject(subject))
        {
            throw Refuse(environmentName, $"{Section}:Subject must be a mailto or https address of the operator.");
        }

        return new WebPushSettings(true, publicKey, privateKey, subject, hosts, max);
    }

    /// <summary>
    /// Whether a subscription endpoint is an https address without credentials, on the default
    /// port, whose host is a listed push service or a subdomain of one.
    /// </summary>
    public bool IsAllowedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length > 0 || !uri.IsDefaultPort)
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return AllowedPushHosts.Any(allowed => host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
    }

    private static (string? PublicKey, string? PrivateKey) ReadKeys(IConfigurationSection section, string? environmentName, string contentRootPath)
    {
        var publicKey = section["VapidPublicKey"];
        var privateKey = section["VapidPrivateKey"];
        if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
        {
            return (publicKey.Trim(), privateKey.Trim());
        }

        var file = section["VapidKeyFile"];
        if (string.IsNullOrWhiteSpace(file))
        {
            return (null, null);
        }

        var path = Path.GetFullPath(file, contentRootPath);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return (document.RootElement.GetProperty("publicKey").GetString(), document.RootElement.GetProperty("privateKey").GetString());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Refuse(environmentName, $"{Section}:VapidKeyFile names '{path}', which is not a JSON file with the members publicKey and privateKey.");
        }
    }

    private static bool IsContactSubject(string? subject) =>
        subject is not null
        && ((subject.StartsWith("mailto:", StringComparison.Ordinal) && subject.Length > "mailto:".Length && subject.Contains('@'))
            || (Uri.TryCreate(subject, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0));

    private static InvalidOperationException Refuse(string? environmentName, string reason) =>
        new($"Invalid Web Push configuration for environment '{environmentName}': {reason}");
}
