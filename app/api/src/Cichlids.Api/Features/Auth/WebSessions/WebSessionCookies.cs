using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>A login in progress: the state and PKCE verifier sent to Keycloak and the web app address to return to.</summary>
public sealed record PendingLogin(string State, string Verifier, string ReturnUrl);

/// <summary>
/// Reads and writes the two cookies of the web session: the persistent session cookie with the
/// session id and the short-lived login cookie with a pending login. Both are httpOnly and
/// SameSite=Lax, host-only for the path /, and Secure with the host prefix unless the settings
/// turn that off for plain-HTTP development.
/// </summary>
public static class WebSessionCookies
{
    /// <summary>
    /// The lifetime of the session cookie, the longest browsers accept; the web app renews it
    /// whenever it opens, so the session lasts until sign-out.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(400);

    /// <summary>How long a login in progress waits for the return from Keycloak.</summary>
    public static readonly TimeSpan LoginLifetime = TimeSpan.FromMinutes(10);

    public static string? ReadSession(HttpRequest request, WebSessionSettings settings) =>
        request.Cookies.TryGetValue(settings.SessionCookieName, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static void WriteSession(HttpResponse response, WebSessionSettings settings, string sessionId) =>
        response.Cookies.Append(settings.SessionCookieName, sessionId, Options(settings, SessionLifetime));

    public static void ClearSession(HttpResponse response, WebSessionSettings settings) =>
        response.Cookies.Delete(settings.SessionCookieName, Options(settings, null));

    public static void WriteLogin(HttpResponse response, WebSessionSettings settings, PendingLogin login)
    {
        var value = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(login));
        response.Cookies.Append(settings.LoginCookieName, value, Options(settings, LoginLifetime));
    }

    /// <summary>The pending login of the login cookie, or null when the cookie is missing or unreadable.</summary>
    public static PendingLogin? ReadLogin(HttpRequest request, WebSessionSettings settings)
    {
        if (!request.Cookies.TryGetValue(settings.LoginCookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var login = JsonSerializer.Deserialize<PendingLogin>(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)));
            return login is { State.Length: > 0, Verifier.Length: > 0, ReturnUrl.Length: > 0 } ? login : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public static void ClearLogin(HttpResponse response, WebSessionSettings settings) =>
        response.Cookies.Delete(settings.LoginCookieName, Options(settings, null));

    private static CookieOptions Options(WebSessionSettings settings, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = settings.SecureCookies,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
}

/// <summary>
/// Decides where a login may return to: a path of the web app on the API's own origin, or an
/// address on one of the allowed web app origins. Anything else would turn the login into an open
/// redirect.
/// </summary>
public static class ReturnUrls
{
    public static bool IsAllowed(string? returnUrl, WebSessionSettings settings)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return false;
        }

        if (returnUrl[0] == '/')
        {
            // "//host" and "/\host" are addresses on another host.
            return returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\');
        }

        return Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && settings.AllowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
    }
}
