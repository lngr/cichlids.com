using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.Keycloak;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Imports the staging realm file into a fresh Keycloak and reads the result back through the
/// admin API. The realm file is what an operator imports into a deployed Keycloak, so the
/// running server has to show the settings that protect a deployment: transport security, closed
/// registration, no password grant on the public client and no accounts.
/// </summary>
[Trait("Story", "task-2.27")]
public sealed class DeployedRealmImportTests(DeployedRealmImportTests.StagingKeycloakFixture keycloak)
    : IClassFixture<DeployedRealmImportTests.StagingKeycloakFixture>
{
    private const string Realm = "cichlids";

    [Fact]
    public async Task Realm_requires_https_and_keeps_registration_closed()
    {
        var realm = await keycloak.GetAsync($"admin/realms/{Realm}");

        Assert.Equal("all", realm.GetProperty("sslRequired").GetString());
        Assert.False(realm.GetProperty("registrationAllowed").GetBoolean());
        Assert.True(realm.GetProperty("verifyEmail").GetBoolean());
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(realm.GetProperty("duplicateEmailsAllowed").GetBoolean());
    }

    [Fact]
    public async Task Public_client_has_no_password_grant_and_only_the_staging_redirects()
    {
        var clients = await keycloak.GetAsync($"admin/realms/{Realm}/clients?clientId=cichlids-app");
        var app = Assert.Single(clients.EnumerateArray().ToList());

        Assert.True(app.GetProperty("publicClient").GetBoolean());
        Assert.False(app.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.False(app.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.Equal(
            ["cichlids://auth", "https://staging.cichlids.com/auth"],
            Strings(app.GetProperty("redirectUris")));
        Assert.Equal(["https://staging.cichlids.com"], Strings(app.GetProperty("webOrigins")));
    }

    [Fact]
    [Trait("Story", "task-3.41")]
    public async Task Offline_sessions_have_no_maximum_lifespan_and_an_idle_timeout_of_ten_years()
    {
        var realm = await keycloak.GetAsync($"admin/realms/{Realm}");

        Assert.False(realm.GetProperty("offlineSessionMaxLifespanEnabled").GetBoolean());
        Assert.Equal(10 * 365 * 24 * 60 * 60, realm.GetProperty("offlineSessionIdleTimeout").GetInt32());
    }

    [Fact]
    [Trait("Story", "task-3.41")]
    public async Task Public_client_offers_the_offline_access_scope()
    {
        var clients = await keycloak.GetAsync($"admin/realms/{Realm}/clients?clientId=cichlids-app");
        var app = Assert.Single(clients.EnumerateArray().ToList());

        Assert.Contains("offline_access", Strings(app.GetProperty("optionalClientScopes")));
    }

    [Fact]
    public async Task Realm_has_no_accounts()
    {
        var count = await keycloak.GetAsync($"admin/realms/{Realm}/users/count");

        Assert.Equal(0, count.GetInt32());
    }

    [Fact]
    public async Task Login_over_plain_http_is_refused()
    {
        using var http = new HttpClient();
        var url = new Uri(
            keycloak.BaseUrl,
            $"realms/{Realm}/protocol/openid-connect/auth?client_id=cichlids-app&response_type=code&scope=openid&redirect_uri=cichlids%3A%2F%2Fauth");

        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS required", body);
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Starts only a Keycloak that imports the staging realm file on startup.
    /// </summary>
    public sealed class StagingKeycloakFixture : IAsyncLifetime
    {
        private const string AdminUser = "admin";
        private const string AdminPassword = "admin";

        private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.6.4")
            .WithUsername(AdminUser)
            .WithPassword(AdminPassword)
            .WithRealm(Path.Combine(AppContext.BaseDirectory, "Fixtures", "staging", "cichlids-realm.json"))
            .Build();

        private readonly HttpClient _http = new();

        public Uri BaseUrl => new(_keycloak.GetBaseAddress());

        public Task InitializeAsync() => _keycloak.StartAsync();

        public async Task DisposeAsync()
        {
            _http.Dispose();
            await _keycloak.DisposeAsync();
        }

        public async Task<JsonElement> GetAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUrl, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        private async Task<string> TokenAsync()
        {
            using var response = await _http.PostAsync(
                new Uri(BaseUrl, "realms/master/protocol/openid-connect/token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = AdminUser,
                    ["password"] = AdminPassword,
                }));
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("access_token").GetString()!;
        }
    }
}
