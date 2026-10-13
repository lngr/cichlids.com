namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>
/// The validated settings of the web app session from the WebSession configuration section.
/// Without a callback URL the API offers no web sign-in; with one it needs the secret of its
/// confidential Keycloak client and the web app address that Keycloak returns to after a sign-out,
/// on one of the allowed origins. Outside the Development environment the
/// cookies have the Secure flag and the host prefix, and the callback and the allowed origins
/// are HTTPS, so a misconfigured deployment fails at startup.
/// </summary>
/// <param name="CallbackUrl">The address Keycloak redirects to after a login, or null when web sign-in is off.</param>
/// <param name="AllowedOrigins">The web app origins a login may return to and a cookie-authenticated write may come from.</param>
/// <param name="SecureCookies">Whether the cookies have the Secure flag and the host prefix.</param>
/// <param name="ClientId">The confidential Keycloak client of the login.</param>
/// <param name="ClientSecret">The secret of the client, or null when web sign-in is off.</param>
/// <param name="PostLogoutRedirectUrl">The web app address Keycloak returns to after a sign-out, or null when web sign-in is off.</param>
public sealed record WebSessionSettings(
    Uri? CallbackUrl,
    IReadOnlyList<string> AllowedOrigins,
    bool SecureCookies,
    string ClientId,
    string? ClientSecret,
    Uri? PostLogoutRedirectUrl)
{
    private const string CallbackUrlKey = "WebSession:CallbackUrl";
    private const string AllowedOriginsKey = "WebSession:AllowedOrigins";
    private const string SecureCookiesKey = "WebSession:SecureCookies";
    private const string ClientIdKey = "WebSession:ClientId";
    private const string ClientSecretKey = "WebSession:ClientSecret";
    private const string PostLogoutRedirectUrlKey = "WebSession:PostLogoutRedirectUrl";
    private const string DefaultClientId = "cichlids-web-bff";

    /// <summary>The cookie with the session id.</summary>
    public string SessionCookieName => SecureCookies ? "__Host-cichlids-session" : "cichlids-session";

    /// <summary>The short-lived cookie with the state, PKCE verifier and return address of a login in progress.</summary>
    public string LoginCookieName => SecureCookies ? "__Host-cichlids-login" : "cichlids-login";

    /// <summary>
    /// Reads the settings for the given host environment name and throws an exception naming the
    /// configuration key when a value is invalid for that environment.
    /// </summary>
    public static WebSessionSettings Resolve(IConfiguration configuration, string? environmentName)
    {
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        var secureCookies = configuration.GetValue(SecureCookiesKey, true);
        if (!secureCookies && !isDevelopment)
        {
            throw Refuse(environmentName, $"{SecureCookiesKey} must not be false; only the Development environment may serve cookies over plain HTTP.");
        }

        Uri? callbackUrl = null;
        var callback = configuration[CallbackUrlKey];
        if (!string.IsNullOrWhiteSpace(callback))
        {
            if (!Uri.TryCreate(callback, UriKind.Absolute, out callbackUrl)
                || (callbackUrl.Scheme != Uri.UriSchemeHttps && (!isDevelopment || callbackUrl.Scheme != Uri.UriSchemeHttp)))
            {
                throw Refuse(environmentName, $"{CallbackUrlKey} must be an absolute HTTPS URL but is '{callback}'.");
            }
        }

        var origins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (!IsOrigin(origin, isDevelopment))
            {
                throw Refuse(environmentName, $"{AllowedOriginsKey} must list origins such as https://cichlids.com but contains '{origin}'.");
            }
        }

        var clientId = configuration[ClientIdKey];
        var clientSecret = configuration[ClientSecretKey];
        if (callbackUrl is not null && string.IsNullOrWhiteSpace(clientSecret))
        {
            throw Refuse(environmentName, $"{ClientSecretKey} is not set. Web sign-in needs the secret of the confidential Keycloak client.");
        }

        Uri? postLogoutRedirectUrl = null;
        var postLogout = configuration[PostLogoutRedirectUrlKey];
        if (callbackUrl is not null)
        {
            if (!Uri.TryCreate(postLogout, UriKind.Absolute, out postLogoutRedirectUrl)
                || !IsOrigin(postLogoutRedirectUrl.GetLeftPart(UriPartial.Authority), isDevelopment)
                || !origins.Contains(postLogoutRedirectUrl.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal))
            {
                throw Refuse(environmentName, $"{PostLogoutRedirectUrlKey} must be an absolute HTTPS URL on one of the {AllowedOriginsKey} but is '{postLogout}'.");
            }
        }

        return new WebSessionSettings(
            callbackUrl,
            origins.Distinct(StringComparer.Ordinal).ToList(),
            secureCookies,
            string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId,
            string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret,
            postLogoutRedirectUrl);
    }

    // Keeps the client secret out of the text of the settings, which may reach a log.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"CallbackUrl = {CallbackUrl}, AllowedOrigins = [{string.Join(", ", AllowedOrigins)}], ");
        builder.Append($"SecureCookies = {SecureCookies}, ClientId = {ClientId}, PostLogoutRedirectUrl = {PostLogoutRedirectUrl}");
        return true;
    }

    private static bool IsOrigin(string value, bool isDevelopment) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.Scheme == Uri.UriSchemeHttp))
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), value, StringComparison.Ordinal);

    private static InvalidOperationException Refuse(string? environmentName, string reason) =>
        new($"Invalid web session configuration for environment '{environmentName}': {reason}");
}
