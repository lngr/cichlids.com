using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Talks to the Keycloak admin REST API of one realm for the legacy account import: ensures the
/// realm prerequisites the imported accounts rely on, lists the existing users, and creates
/// missing users in chunks. Authenticates as the master realm admin through admin-cli and fetches
/// a fresh token shortly before the current one expires, so runs over many thousand users keep
/// working with short token lifetimes.
/// </summary>
public sealed class KeycloakAdminClient : IDisposable
{
    /// <summary>
    /// The default number of users read in one user list page.
    /// </summary>
    public const int DefaultUserPageSize = 500;

    /// <summary>
    /// The default number of users sent in one partial import request.
    /// </summary>
    public const int DefaultImportChunkSize = 500;

    private const string TermsAndConditionsAction = "TERMS_AND_CONDITIONS";
    private const string NotConfigured = "not-configured";
    private const string EmailClientScope = "email";
    private const string EmailVerifiedClaim = "email_verified";

    // A token is replaced this long before its reported expiry, so a request never starts with a
    // token that runs out while Keycloak processes it.
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly KeycloakAdminSettings _settings;
    private readonly string _serverUrl;
    private readonly int _userPageSize;
    private readonly int _importChunkSize;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public KeycloakAdminClient(KeycloakAdminSettings settings)
        : this(settings, DefaultUserPageSize, DefaultImportChunkSize)
    {
    }

    internal KeycloakAdminClient(KeycloakAdminSettings settings, int userPageSize, int importChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(userPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(importChunkSize, 1);

        _settings = settings;
        _userPageSize = userPageSize;
        _importChunkSize = importChunkSize;
        _serverUrl = settings.BaseUrl.ToString().TrimEnd('/');

        // A partial import of a full chunk takes a while on a busy server.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>
    /// Makes sure the realm has the terms and conditions required action registered and enabled,
    /// has the google and facebook identity providers that federated identity links refer to,
    /// and puts the email_verified claim into tokens through its email client scope. A missing
    /// provider is created disabled with placeholder credentials, a missing claim mapper is added;
    /// existing providers and mappers are left as they are.
    /// </summary>
    public async Task EnsureRealmPrerequisitesAsync(CancellationToken cancellationToken)
    {
        await EnsureTermsAndConditionsAsync(cancellationToken);
        await EnsureEmailVerifiedMapperAsync(cancellationToken);
        await EnsureIdentityProviderAsync("google", "google", cancellationToken);
        await EnsureIdentityProviderAsync("facebook", "facebook", cancellationToken);
    }

    /// <summary>
    /// Reads every user of the realm, page by page, into lookups by lowercased email and by
    /// lowercased username.
    /// </summary>
    public async Task<KeycloakUserIndex> LoadUsersAsync(CancellationToken cancellationToken)
    {
        var byEmail = new Dictionary<string, ExistingKeycloakUser>(StringComparer.Ordinal);
        var byUsername = new Dictionary<string, ExistingKeycloakUser>(StringComparer.Ordinal);

        for (var first = 0; ; first += _userPageSize)
        {
            using var response = await SendAsync(
                HttpMethod.Get, $"users?briefRepresentation=true&first={first}&max={_userPageSize}", null, cancellationToken);
            await EnsureSuccessAsync(response, "list users", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<List<UserSummary>>(JsonOptions, cancellationToken) ?? [];
            foreach (var summary in page)
            {
                var user = new ExistingKeycloakUser(summary.Id, summary.EmailVerified);
                if (!string.IsNullOrEmpty(summary.Email))
                {
                    byEmail.TryAdd(summary.Email.Trim().ToLowerInvariant(), user);
                }

                byUsername.TryAdd(summary.Username.ToLowerInvariant(), user);
            }

            if (page.Count < _userPageSize)
            {
                return new KeycloakUserIndex(byEmail, byUsername);
            }
        }
    }

    /// <summary>
    /// Creates the given accounts through partial imports of at most one import chunk of users
    /// each, with their planned ids, required actions and federated identity links. Users that already exist
    /// are skipped by Keycloak and left unchanged. Returns the number of users Keycloak added.
    /// </summary>
    public async Task<int> CreateUsersAsync(IReadOnlyList<PlannedAccount> accounts, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var chunk in accounts.Chunk(_importChunkSize))
        {
            var body = new PartialImportRequest(
                "SKIP",
                chunk.Select(ToRepresentation).ToList());

            using var response = await SendAsync(HttpMethod.Post, "partialImport", body, cancellationToken);
            await EnsureSuccessAsync(response, "partial import", cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<PartialImportResponse>(JsonOptions, cancellationToken);
            added += result?.Added ?? 0;
        }

        return added;
    }

    public void Dispose() => _http.Dispose();

    private static UserRepresentation ToRepresentation(PlannedAccount account) => new(
        account.KeycloakUserId.ToString(),
        account.Username,
        account.Email,
        account.EmailVerified,
        true,
        account.RequiredActions,
        account.FederatedIdentities
            .Select(link => new FederatedIdentityRepresentation(link.Alias, link.UserId, link.UserId))
            .ToList());

    private async Task EnsureTermsAndConditionsAsync(CancellationToken cancellationToken)
    {
        using (var list = await SendAsync(HttpMethod.Get, "authentication/required-actions", null, cancellationToken))
        {
            await EnsureSuccessAsync(list, "list required actions", cancellationToken);
            var actions = await list.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions, cancellationToken) ?? [];
            var registered = actions.Any(action =>
                action.TryGetProperty("alias", out var alias) && alias.GetString() == TermsAndConditionsAction);

            if (!registered)
            {
                using var register = await SendAsync(
                    HttpMethod.Post,
                    "authentication/register-required-action",
                    new { providerId = TermsAndConditionsAction, name = "Terms and Conditions" },
                    cancellationToken);
                await EnsureSuccessAsync(register, "register the terms and conditions required action", cancellationToken);
            }
        }

        using var get = await SendAsync(
            HttpMethod.Get, $"authentication/required-actions/{TermsAndConditionsAction}", null, cancellationToken);
        await EnsureSuccessAsync(get, "read the terms and conditions required action", cancellationToken);
        var representation = await get.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Keycloak returned an empty required action representation.");

        if (representation.TryGetValue("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
        {
            return;
        }

        representation["enabled"] = JsonSerializer.SerializeToElement(true);
        using var put = await SendAsync(
            HttpMethod.Put, $"authentication/required-actions/{TermsAndConditionsAction}", representation, cancellationToken);
        await EnsureSuccessAsync(put, "enable the terms and conditions required action", cancellationToken);
    }

    // The API links a login to a migrated profile by email only when the token says the email is
    // verified, so the claim has to be in every token of the realm's email scope.
    private async Task EnsureEmailVerifiedMapperAsync(CancellationToken cancellationToken)
    {
        string scopeId;
        using (var scopes = await SendAsync(HttpMethod.Get, "client-scopes", null, cancellationToken))
        {
            await EnsureSuccessAsync(scopes, "list client scopes", cancellationToken);
            var all = await scopes.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions, cancellationToken) ?? [];
            var emailScope = all.FirstOrDefault(scope =>
                scope.TryGetProperty("name", out var name) && name.GetString() == EmailClientScope);
            if (emailScope.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Realm '{_settings.Realm}' has no client scope named '{EmailClientScope}'.");
            }

            scopeId = emailScope.GetProperty("id").GetString()!;
        }

        using (var mappers = await SendAsync(
                   HttpMethod.Get, $"client-scopes/{scopeId}/protocol-mappers/models", null, cancellationToken))
        {
            await EnsureSuccessAsync(mappers, "list email scope mappers", cancellationToken);
            var all = await mappers.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions, cancellationToken) ?? [];
            var present = all.Any(mapper =>
                mapper.TryGetProperty("config", out var config)
                && config.TryGetProperty("claim.name", out var claim)
                && claim.GetString() == EmailVerifiedClaim);
            if (present)
            {
                return;
            }
        }

        var body = new
        {
            name = "email verified",
            protocol = "openid-connect",
            protocolMapper = "oidc-usermodel-property-mapper",
            config = new Dictionary<string, string>
            {
                ["user.attribute"] = "emailVerified",
                ["claim.name"] = EmailVerifiedClaim,
                ["jsonType.label"] = "boolean",
                ["access.token.claim"] = "true",
                ["id.token.claim"] = "true",
            },
        };

        using var create = await SendAsync(
            HttpMethod.Post, $"client-scopes/{scopeId}/protocol-mappers/models", body, cancellationToken);
        await EnsureSuccessAsync(create, "add the email_verified mapper", cancellationToken);
    }

    private async Task EnsureIdentityProviderAsync(string alias, string providerId, CancellationToken cancellationToken)
    {
        using (var get = await SendAsync(HttpMethod.Get, $"identity-provider/instances/{alias}", null, cancellationToken))
        {
            if (get.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(get, $"read identity provider {alias}", cancellationToken);
                return;
            }
        }

        var body = new
        {
            alias,
            providerId,
            enabled = false,
            config = new Dictionary<string, string>
            {
                ["clientId"] = NotConfigured,
                ["clientSecret"] = NotConfigured,
            },
        };

        using var create = await SendAsync(HttpMethod.Post, "identity-provider/instances", body, cancellationToken);
        await EnsureSuccessAsync(create, $"create identity provider {alias}", cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string realmPath, object? body, CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, realmPath, body, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // A token can be invalidated before its reported expiry, for instance by a restart of the
        // server; one retry with a fresh token covers that.
        response.Dispose();
        _accessToken = null;
        return await SendOnceAsync(method, realmPath, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string realmPath, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method, $"{_serverUrl}/admin/realms/{Uri.EscapeDataString(_settings.Realm)}/{realmPath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt - TokenRefreshMargin)
        {
            return _accessToken;
        }

        var requestedAt = DateTimeOffset.UtcNow;
        using var response = await _http.PostAsync(
            $"{_serverUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = _settings.AdminUser,
                ["password"] = _settings.AdminPassword,
            }),
            cancellationToken);
        await EnsureSuccessAsync(response, "obtain an admin token", cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

        _accessToken = token.AccessToken;
        _accessTokenExpiresAt = requestedAt.AddSeconds(token.ExpiresIn);
        return _accessToken;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Keycloak request to {operation} failed with {(int)response.StatusCode} {response.StatusCode}: {content}");
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserSummary(string Id, string Username, string? Email, bool EmailVerified);

    private sealed record PartialImportRequest(string IfResourceExists, IReadOnlyList<UserRepresentation> Users);

    private sealed record PartialImportResponse(int Added, int Skipped);

    private sealed record UserRepresentation(
        string Id,
        string Username,
        string? Email,
        bool EmailVerified,
        bool Enabled,
        IReadOnlyList<string> RequiredActions,
        IReadOnlyList<FederatedIdentityRepresentation> FederatedIdentities);

    private sealed record FederatedIdentityRepresentation(string IdentityProvider, string UserId, string UserName);
}
