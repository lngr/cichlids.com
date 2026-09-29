namespace Cichlids.Etl.Identity;

/// <summary>
/// Recognises an external avatar URL served from Gravatar. Gravatar's avatar path is the MD5
/// hash of the member's lowercased, trimmed email address, so the URL is a reversible email
/// hash rather than an opaque image reference, and the ETL never stores it as a profile's avatar.
/// </summary>
public static class ExternalAvatarUrl
{
    /// <summary>
    /// Returns true when the value is an absolute URL whose host is gravatar.com or a subdomain
    /// of it, for either http or https. A value that is null or not an absolute URL is not
    /// Gravatar.
    /// </summary>
    public static bool IsGravatar(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && IsGravatarHost(uri.Host);

    private static bool IsGravatarHost(string host) =>
        host.Equals("gravatar.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".gravatar.com", StringComparison.OrdinalIgnoreCase);
}
