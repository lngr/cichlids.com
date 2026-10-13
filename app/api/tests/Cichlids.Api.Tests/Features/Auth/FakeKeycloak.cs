using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// Answers the Keycloak requests of the web session flow in memory: the discovery document, the
/// token endpoint for authorization codes and refresh tokens, and the logout endpoint. Each test
/// registers the answer for its own codes and refresh tokens, so tests sharing one instance stay
/// independent. Token endpoint and logout requests must authenticate the confidential client with
/// its secret in the form; every one of them is recorded with its form fields.
/// </summary>
public sealed class FakeKeycloak : HttpMessageHandler
{
    public const string AuthorizationEndpoint = "https://keycloak.test/realms/cichlids/protocol/openid-connect/auth";
    public const string TokenEndpoint = "https://keycloak.test/realms/cichlids/protocol/openid-connect/token";
    public const string LogoutEndpoint = "https://keycloak.test/realms/cichlids/protocol/openid-connect/logout";

    /// <summary>The confidential client that token and logout requests must authenticate as.</summary>
    public const string ClientId = "cichlids-web-bff";

    public const string ClientSecret = "fake-keycloak-client-secret";

    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _codes = new();
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _refreshTokens = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> TokenRequests { get; } = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> LogoutRequests { get; } = new();

    private readonly ConcurrentDictionary<string, byte> _unreachableLogouts = new();

    /// <summary>Lets the logout of the refresh token fail as if Keycloak were unreachable.</summary>
    public void BeUnreachableForLogoutOf(string refreshToken) => _unreachableLogouts[refreshToken] = 0;

    /// <summary>Lets the logout of the refresh token succeed again.</summary>
    public void BeReachableForLogoutOf(string refreshToken) => _unreachableLogouts.TryRemove(refreshToken, out _);

    /// <summary>The token response for the code, as Keycloak issues it for a fresh login.</summary>
    public void IssueForCode(string code, TokenAnswer answer) => _codes[code] = () => Json(HttpStatusCode.OK, answer.ToJson());

    /// <summary>Lets the refresh token be renewed with the given answer.</summary>
    public void RenewWith(string refreshToken, TokenAnswer answer) =>
        _refreshTokens[refreshToken] = () => Json(HttpStatusCode.OK, answer.ToJson());

    /// <summary>Refuses the refresh token the way Keycloak refuses one of a disabled account.</summary>
    public void Refuse(string refreshToken) =>
        _refreshTokens[refreshToken] = () => Json(
            HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "User disabled" });

    /// <summary>Answers the refresh token with a server error page.</summary>
    public void FailWithServerError(string refreshToken) =>
        _refreshTokens[refreshToken] = () => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad Gateway</html>", Encoding.UTF8, "text/html"),
        };

    /// <summary>Lets every request for the refresh token fail as if Keycloak were unreachable.</summary>
    public void BeUnreachableFor(string refreshToken) =>
        _refreshTokens[refreshToken] = () => throw new HttpRequestException("Connection refused");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (request.Method == HttpMethod.Get && url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new
            {
                issuer = TestTokens.Issuer,
                authorization_endpoint = AuthorizationEndpoint,
                token_endpoint = TokenEndpoint,
                end_session_endpoint = LogoutEndpoint,
            });
        }

        var form = await ReadFormAsync(request, cancellationToken);
        if (request.Method == HttpMethod.Post && url == TokenEndpoint)
        {
            TokenRequests.Enqueue(form);
            if (!IsClientAuthenticated(form))
            {
                return UnauthorizedClient();
            }
            var answer = form.GetValueOrDefault("grant_type") switch
            {
                "authorization_code" => _codes.TryRemove(form.GetValueOrDefault("code") ?? string.Empty, out var issue) ? issue : null,
                "refresh_token" => _refreshTokens.GetValueOrDefault(form.GetValueOrDefault("refresh_token") ?? string.Empty),
                _ => null,
            };
            return answer?.Invoke() ?? Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
        }

        if (request.Method == HttpMethod.Post && url == LogoutEndpoint)
        {
            LogoutRequests.Enqueue(form);
            if (!IsClientAuthenticated(form))
            {
                return UnauthorizedClient();
            }

            if (_unreachableLogouts.ContainsKey(form.GetValueOrDefault("refresh_token") ?? string.Empty))
            {
                throw new HttpRequestException("Connection refused");
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static bool IsClientAuthenticated(IReadOnlyDictionary<string, string> form) =>
        form.GetValueOrDefault("client_id") == ClientId && form.GetValueOrDefault("client_secret") == ClientSecret;

    private static HttpResponseMessage UnauthorizedClient() => Json(
        HttpStatusCode.Unauthorized,
        new { error = "unauthorized_client", error_description = "Invalid client or Invalid client credentials" });

    private static async Task<IReadOnlyDictionary<string, string>> ReadFormAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return new Dictionary<string, string>();
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')), parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}

/// <summary>A token endpoint answer with a five-minute access token.</summary>
public sealed record TokenAnswer(string AccessToken, string RefreshToken, string IdToken = "id-token", int ExpiresIn = 300)
{
    public object ToJson() => new
    {
        access_token = AccessToken,
        refresh_token = RefreshToken,
        id_token = IdToken,
        expires_in = ExpiresIn,
        refresh_expires_in = 0,
        token_type = "Bearer",
        scope = "openid offline_access profile email",
    };
}
