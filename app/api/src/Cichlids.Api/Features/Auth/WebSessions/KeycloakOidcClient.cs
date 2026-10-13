using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cichlids.Api.Features.Auth.WebSessions;

/// <summary>The Keycloak endpoints of the web login, read from the issuer's discovery document.</summary>
public sealed record KeycloakEndpoints(Uri Authorization, Uri Token, Uri? EndSession);

/// <summary>Tokens the Keycloak token endpoint issued; ExpiresIn is the access token lifetime in seconds.</summary>
public sealed record IssuedTokens(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn);

public enum TokenRequestOutcome
{
    /// <summary>Keycloak issued tokens.</summary>
    Issued,

    /// <summary>Keycloak refused the code or refresh token with HTTP 400 or 401.</summary>
    Refused,

    /// <summary>
    /// No usable answer arrived: no connection, a timeout, a server error, an unreadable body, or
    /// Keycloak refused the client credentials of the API.
    /// </summary>
    Unavailable,
}

/// <summary>The result of a token request; Tokens is set for the Issued outcome only.</summary>
public sealed record TokenRequestResult(TokenRequestOutcome Outcome, IssuedTokens? Tokens);

/// <summary>The Keycloak discovery document could not be loaded.</summary>
public sealed class KeycloakUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Talks to Keycloak on behalf of web app sessions as a confidential client: loads and caches the
/// endpoints of the issuer, exchanges login codes, renews tokens with a refresh token and ends
/// sessions, each request authenticated with the client secret. Token requests never throw; their
/// answer is classified into issued, refused (HTTP 400 or 401 for the code or token) and
/// unavailable (everything else, including refused client credentials).
/// </summary>
public sealed class KeycloakOidcClient(
    IHttpClientFactory httpClientFactory,
    AuthenticationSettings authentication,
    WebSessionSettings settings,
    ILogger<KeycloakOidcClient> logger)
{
    public const string HttpClientName = "keycloak";

    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private KeycloakEndpoints? _endpoints;

    /// <summary>
    /// The endpoints from the discovery document, loaded on the first call. A failed load throws
    /// a KeycloakUnavailableException and is tried again on the next call.
    /// </summary>
    public async Task<KeycloakEndpoints> GetEndpointsAsync(CancellationToken cancellationToken)
    {
        if (_endpoints is not null)
        {
            return _endpoints;
        }

        if (string.IsNullOrWhiteSpace(authentication.Authority))
        {
            throw new KeycloakUnavailableException("Authentication:Authority is not set, so the Keycloak endpoints are unknown.");
        }

        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (_endpoints is not null)
            {
                return _endpoints;
            }

            var discoveryUrl = new Uri($"{authentication.Authority.TrimEnd('/')}/.well-known/openid-configuration");
            DiscoveryDocument? document;
            try
            {
                using var response = await Http().GetAsync(discoveryUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                document = await response.Content.ReadFromJsonAsync<DiscoveryDocument>(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new KeycloakUnavailableException($"The Keycloak discovery document at {discoveryUrl} could not be loaded.", exception);
            }

            if (document?.AuthorizationEndpoint is null || document.TokenEndpoint is null)
            {
                throw new KeycloakUnavailableException($"The Keycloak discovery document at {discoveryUrl} names no authorization or token endpoint.");
            }

            _endpoints = new KeycloakEndpoints(document.AuthorizationEndpoint, document.TokenEndpoint, document.EndSessionEndpoint);
            return _endpoints;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>Exchanges the code of a login for tokens, proving the login with the PKCE verifier.</summary>
    public Task<TokenRequestResult> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken) =>
        RequestTokensAsync(
            ClientForm(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri.AbsoluteUri,
            }),
            cancellationToken);

    /// <summary>Renews the tokens with the given refresh token.</summary>
    public Task<TokenRequestResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(
            ClientForm(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
            cancellationToken);

    /// <summary>
    /// Ends the Keycloak session of the refresh token, which revokes an offline token. Returns true
    /// when no session of the token remains in Keycloak: it ended the session, refused the token
    /// as already invalid, or offers no logout endpoint. Returns false when Keycloak gave no usable
    /// answer (no connection, a timeout or a server error) or refused the client credentials, in
    /// which case the failure is logged and the session stays in Keycloak until its idle timeout.
    /// </summary>
    public async Task<bool> EndSessionAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var endpoints = await GetEndpointsAsync(cancellationToken);
            if (endpoints.EndSession is null)
            {
                return true;
            }

            using var content = new FormUrlEncodedContent(ClientForm(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
            }));
            using var response = await Http().PostAsync(endpoints.EndSession, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if ((int)response.StatusCode is >= 400 and < 500 && await RejectsClientAsync(response, cancellationToken))
            {
                logger.LogError("Keycloak refused the client credentials of the API ({Status}).", (int)response.StatusCode);
                return false;
            }

            logger.LogWarning("Keycloak answered the logout of a web session with {Status}.", (int)response.StatusCode);
            return (int)response.StatusCode is >= 400 and < 500;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or KeycloakUnavailableException)
        {
            logger.LogWarning(exception, "The Keycloak session of a web session could not be ended.");
            return false;
        }
    }

    private async Task<TokenRequestResult> RequestTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            var endpoints = await GetEndpointsAsync(cancellationToken);
            using var content = new FormUrlEncodedContent(form);
            using var response = await Http().PostAsync(endpoints.Token, content, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
            {
                // Refused client credentials are a fault of the API's configuration; the session
                // itself may be fine and stays.
                if (await RejectsClientAsync(response, cancellationToken))
                {
                    logger.LogError("Keycloak refused the client credentials of the API ({Status}).", (int)response.StatusCode);
                    return new TokenRequestResult(TokenRequestOutcome.Unavailable, null);
                }

                return new TokenRequestResult(TokenRequestOutcome.Refused, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The Keycloak token endpoint answered {Status}.", (int)response.StatusCode);
                return new TokenRequestResult(TokenRequestOutcome.Unavailable, null);
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (string.IsNullOrEmpty(body?.AccessToken))
            {
                return new TokenRequestResult(TokenRequestOutcome.Unavailable, null);
            }

            return new TokenRequestResult(
                TokenRequestOutcome.Issued,
                new IssuedTokens(body.AccessToken, body.RefreshToken, body.IdToken, body.ExpiresIn ?? 0));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeycloakUnavailableException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(exception, "The Keycloak token endpoint could not be reached.");
            return new TokenRequestResult(TokenRequestOutcome.Unavailable, null);
        }
    }

    private HttpClient Http() => httpClientFactory.CreateClient(HttpClientName);

    // Authenticates the confidential client with its secret in the form (client_secret_post).
    private Dictionary<string, string> ClientForm(Dictionary<string, string> form)
    {
        form["client_id"] = settings.ClientId;
        if (settings.ClientSecret is not null)
        {
            form["client_secret"] = settings.ClientSecret;
        }

        return form;
    }

    private static async Task<bool> RejectsClientAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
            return error?.Error is "invalid_client" or "unauthorized_client";
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record ErrorResponse([property: JsonPropertyName("error")] string? Error);

    private sealed record DiscoveryDocument(
        [property: JsonPropertyName("authorization_endpoint")] Uri? AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] Uri? TokenEndpoint,
        [property: JsonPropertyName("end_session_endpoint")] Uri? EndSessionEndpoint);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);
}
