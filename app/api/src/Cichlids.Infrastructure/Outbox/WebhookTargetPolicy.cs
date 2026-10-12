using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Decides whether a URL is an acceptable webhook target: an absolute http or https URL without
/// user information whose host is on the configured allowlist. Hosts are compared in their ASCII
/// form, exactly and case-insensitively, so an internationalized name and its punycode spelling
/// are the same host and a subdomain of a listed host is a different one.
/// </summary>
public sealed class WebhookTargetPolicy
{
    private static readonly IdnMapping Idn = new();

    private readonly HashSet<string> _allowedHosts;

    public WebhookTargetPolicy(IEnumerable<string> allowedHosts)
    {
        _allowedHosts = allowedHosts
            .Where(IsBareHostName)
            .Select(NormalizeEntry)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The allowlisted hosts in their normalized ASCII form, sorted for display.
    /// </summary>
    public IReadOnlyList<string> AllowedHosts => _allowedHosts.Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Returns true when the URL is an allowed webhook target. Otherwise returns false with a
    /// reason that is safe to show to the administrator who submitted the URL.
    /// </summary>
    public bool IsAllowed([NotNullWhen(true)] string? url, [NotNullWhen(false)] out string? reason)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            reason = "The webhook url must be an absolute http or https url.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = "The webhook url must use http or https.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "The webhook url must not contain user information.";
            return false;
        }

        if (!_allowedHosts.Contains(HostOf(uri)))
        {
            reason = "The webhook url's host is not on this environment's webhook target allowlist.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Returns true when the entry is a bare DNS name, IPv4 address or IPv6 address, with no
    /// scheme, port, path or wildcard.
    /// </summary>
    public static bool IsBareHostName(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || entry != entry.Trim())
        {
            return false;
        }

        if (Uri.CheckHostName(entry) is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return true;
        }

        try
        {
            return Uri.CheckHostName(Idn.GetAscii(entry)) == UriHostNameType.Dns;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizeEntry(string entry) =>
        IPAddress.TryParse(entry, out var address)
            ? address.ToString()
            : Idn.GetAscii(entry).ToLowerInvariant();

    private static string HostOf(Uri uri)
    {
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.IdnHost;
        return IPAddress.TryParse(host, out var address) ? address.ToString() : host.ToLowerInvariant();
    }
}
