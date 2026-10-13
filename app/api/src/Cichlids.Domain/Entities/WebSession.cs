namespace Cichlids.Domain.Entities;

/// <summary>
/// The server side of a web app session: the Keycloak tokens of one browser, encrypted with the
/// key ring of the API, found through the hash of the random session id the browser holds in its
/// cookie. The row lives until the member signs out or Keycloak refuses to renew the tokens.
/// </summary>
public class WebSession
{
    public long Id { get; set; }

    /// <summary>SHA-256 of the session id in the cookie; the session id itself is never stored.</summary>
    public byte[] SessionIdHash { get; set; } = [];

    /// <summary>The Keycloak account id, the subject of the tokens.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The encrypted access token.</summary>
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>When the access token expires.</summary>
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>The encrypted offline refresh token that renews the access token.</summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    /// <summary>The encrypted id token of the login, or null when Keycloak issued none.</summary>
    public string? EncryptedIdToken { get; set; }

    /// <summary>The token that state-changing requests of this session send in a request header.</summary>
    public string CsrfToken { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the web app last opened with this session.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
