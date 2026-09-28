// Story: task-3.15
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Runs the keycloak-import command twice against real MySQL, Postgres and Keycloak containers
/// seeded with a small legacy scenario, then checks the resulting Keycloak users and profile
/// identities through the admin API and SQL.
/// </summary>
public sealed class KeycloakAccountImportTests(KeycloakAccountImportTests.Scenario scenario)
    : IClassFixture<KeycloakAccountImportTests.Scenario>
{
    private const string UpdatePassword = "UPDATE_PASSWORD";
    private const string TermsAndConditions = "TERMS_AND_CONDITIONS";

    [Fact]
    public async Task CreatesEveryKnownEmailExactlyOnceAndMergesDuplicateEmails()
    {
        foreach (var email in new[] { "alice@example.test", "bob@example.test", "carol@example.test", "dave@example.test", "grace@example.test", "ivy@example.test", "judy@example.test" })
        {
            var matches = await scenario.Admin.GetArrayAsync($"users?email={Uri.EscapeDataString(email)}&exact=true");
            Assert.True(matches.Count == 1, $"expected exactly one user with email {email}, found {matches.Count}");
        }

        // Five email accounts and the email-less facebook account created by the import, the four
        // pre-existing users and the realm's two seeded dev users.
        var all = await scenario.Admin.GetArrayAsync("users?briefRepresentation=true&first=0&max=100");
        Assert.Equal(12, all.Count);
    }

    [Fact]
    public async Task NeitherCreatesNorLinksAnAccountWhoseEmailIsTakenAsAnotherUsersUsername()
    {
        Assert.Empty(await scenario.Admin.GetArrayAsync("users?email=victor%40example.test&exact=true"));

        var squatter = await scenario.Admin.GetObjectAsync($"users/{scenario.PreexistingSquatterId}");
        Assert.Equal("victor@example.test", squatter.GetProperty("username").GetString());
        Assert.Empty(squatter.GetProperty("requiredActions").EnumerateArray());

        await using var db = scenario.Fixture.CreateTargetContext();
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.ProfileId == scenario.ProfileIds["victor"]));
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.Subject == scenario.PreexistingSquatterId));
    }

    [Fact]
    public async Task DoesNotLinkAUserHoldingTheUsernameOfAnEmaillessAccount()
    {
        var squatter = await scenario.Admin.GetObjectAsync($"users/{scenario.PreexistingEmaillessSquatterId}");
        Assert.Equal("facebook-fb-heidi", squatter.GetProperty("username").GetString());
        Assert.Empty(squatter.GetProperty("requiredActions").EnumerateArray());
        Assert.Empty(await FederatedIdentitiesAsync(scenario.PreexistingEmaillessSquatterId));

        await using var db = scenario.Fixture.CreateTargetContext();
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.ProfileId == scenario.ProfileIds["heidi"]));
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.Subject == scenario.PreexistingEmaillessSquatterId));
    }

    [Fact]
    public async Task DoesNotLinkAPreexistingUserWithAnUnverifiedLegacyEmail()
    {
        var judy = await scenario.Admin.GetObjectAsync($"users/{scenario.PreexistingUnverifiedJudyId}");
        Assert.False(judy.GetProperty("emailVerified").GetBoolean());
        Assert.Empty(judy.GetProperty("requiredActions").EnumerateArray());

        await using var db = scenario.Fixture.CreateTargetContext();
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.ProfileId == scenario.ProfileIds["judy"]));
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.Provider == "oidc" && i.Subject == scenario.PreexistingUnverifiedJudyId));
    }

    [Fact]
    public async Task AssignsTheDeterministicIdToACreatedUser()
    {
        var expectedId = Uuidv5.Create(LegacyAccountPlanner.NamespaceId, "alice@example.test");

        var user = await scenario.Admin.GetObjectAsync($"users/{expectedId}");

        Assert.Equal("alice@example.test", user.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GivesPasswordUsersUpdatePasswordAndTermsAndSocialOnlyUsersOnlyTerms()
    {
        Assert.Equal([TermsAndConditions, UpdatePassword], await RequiredActionsAsync("alice@example.test"));
        Assert.Equal([TermsAndConditions, UpdatePassword], await RequiredActionsAsync("bob@example.test"));
        Assert.Equal([TermsAndConditions], await RequiredActionsAsync("carol@example.test"));
        Assert.Equal([TermsAndConditions], await RequiredActionsAsync("dave@example.test"));
    }

    [Fact]
    public async Task LinksGoogleAndFacebookIdentitiesExceptOnesWithAnUnverifiedEmail()
    {
        Assert.Empty(await FederatedIdentitiesAsync(await UserIdByEmailAsync("alice@example.test")));
        Assert.Equal([("google", "g-carol")], await FederatedIdentitiesAsync(await UserIdByEmailAsync("carol@example.test")));
        Assert.Equal([("facebook", "fb-dave")], await FederatedIdentitiesAsync(await UserIdByEmailAsync("dave@example.test")));
        Assert.Equal([("facebook", "fb-erin")], await FederatedIdentitiesAsync(await UserIdByUsernameAsync("facebook-fb-erin")));
    }

    [Fact]
    public async Task ImportsTheEmaillessAccountWithAProfileAndLeavesOutTheOneWithout()
    {
        var erin = await scenario.Admin.GetArrayAsync("users?username=facebook-fb-erin&exact=true");
        Assert.Single(erin);

        var frank = await scenario.Admin.GetArrayAsync("users?search=fb-frank");
        Assert.Empty(frank);
    }

    [Fact]
    public async Task LinksEveryAccountWithAProfileThroughAnOidcIdentity()
    {
        var aliceId = await UserIdByEmailAsync("alice@example.test");
        var carolId = await UserIdByEmailAsync("carol@example.test");
        var erinId = await UserIdByUsernameAsync("facebook-fb-erin");
        var daveId = await UserIdByEmailAsync("dave@example.test");

        await using var db = scenario.Fixture.CreateTargetContext();
        var oidc = await db.ProfileIdentities.Where(i => i.Provider == "oidc").ToListAsync();

        Assert.Contains(oidc, i => i.Subject == aliceId && i.ProfileId == scenario.ProfileIds["alice"]);
        Assert.Contains(oidc, i => i.Subject == daveId && i.ProfileId == scenario.ProfileIds["dave"]);
        Assert.Contains(oidc, i => i.Subject == carolId && i.ProfileId == scenario.ProfileIds["carol"]);
        Assert.Contains(oidc, i => i.Subject == erinId && i.ProfileId == scenario.ProfileIds["erin"]);
        Assert.Equal(oidc.Count, oidc.Select(i => i.Subject).Distinct().Count());
    }

    [Fact]
    public async Task LeavesAPreexistingKeycloakUserUntouchedAndLinksItsProfile()
    {
        var grace = await scenario.Admin.GetObjectAsync($"users/{scenario.PreexistingGraceId}");
        Assert.Equal("grace-self", grace.GetProperty("username").GetString());
        Assert.Empty(grace.GetProperty("requiredActions").EnumerateArray());

        await using var db = scenario.Fixture.CreateTargetContext();
        var link = await db.ProfileIdentities.SingleAsync(i => i.Provider == "oidc" && i.Subject == scenario.PreexistingGraceId);
        Assert.Equal(scenario.ProfileIds["grace"], link.ProfileId);
    }

    [Fact]
    public async Task LeavesAnOidcIdentityOfAnotherProfileUntouched()
    {
        var ivyId = await UserIdByEmailAsync("ivy@example.test");

        await using var db = scenario.Fixture.CreateTargetContext();
        var links = await db.ProfileIdentities.Where(i => i.Provider == "oidc" && i.Subject == ivyId).ToListAsync();

        var link = Assert.Single(links);
        Assert.Equal(scenario.ProfileIds["ivy-other"], link.ProfileId);
    }

    [Fact]
    public void ReportsCreatedUsersOnTheFirstRunAndOnlyExistingUsersOnTheSecond()
    {
        // Dave's imported user has an unverified email; it counts as the import's own user on the
        // second run and keeps its profile link.
        var first = scenario.FirstRun;
        Assert.Equal(12, first.RecordsRead);
        Assert.Equal(10, first.AccountsPlanned);
        Assert.Equal(6, first.Created);
        Assert.Equal(1, first.Existing);
        Assert.Equal(1, first.DroppedByReason["no_contact_no_profile"]);
        Assert.Equal(1, first.AccountConflictsByReason["existing_unverified"]);
        Assert.Equal(2, first.AccountConflictsByReason["username_taken"]);
        Assert.Equal(5, first.ProfileLinksInserted);
        Assert.Equal(0, first.ProfileLinksPresent);
        Assert.Equal(1, first.ProfileLinkConflicts);
        Assert.Equal(0, first.Unresolved);

        var second = scenario.SecondRun;
        Assert.Equal(10, second.AccountsPlanned);
        Assert.Equal(0, second.Created);
        Assert.Equal(7, second.Existing);
        Assert.Equal(1, second.AccountConflictsByReason["existing_unverified"]);
        Assert.Equal(2, second.AccountConflictsByReason["username_taken"]);
        Assert.Equal(0, second.ProfileLinksInserted);
        Assert.Equal(5, second.ProfileLinksPresent);
        Assert.Equal(1, second.ProfileLinkConflicts);
        Assert.Equal(0, second.Unresolved);
    }

    [Fact]
    public async Task RejectsAPasswordGrantForAnImportedPasswordUserUntilTheAccountIsSetUp()
    {
        var ivyId = await UserIdByEmailAsync("ivy@example.test");
        using (var reset = await scenario.Admin.PutAsync(
                   $"users/{ivyId}/reset-password",
                   new { type = "password", value = "Ivy-import-check-1", temporary = false }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using var http = new HttpClient();
        using var response = await http.PostAsync(
            new Uri(scenario.Fixture.KeycloakUrl, $"realms/{KeycloakImportFixture.Realm}/protocol/openid-connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "cichlids-app",
                ["username"] = "ivy@example.test",
                ["password"] = "Ivy-import-check-1",
            }));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", body.GetProperty("error").GetString());
        Assert.Contains("Account is not fully set up", body.GetProperty("error_description").GetString());
    }

    [Fact]
    public async Task IssuesTheEmailVerifiedClaimTheApiLinksMigratedProfilesBy()
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync(
            new Uri(scenario.Fixture.KeycloakUrl, $"realms/{KeycloakImportFixture.Realm}/protocol/openid-connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "cichlids-app",
                ["username"] = "dev-user",
                ["password"] = "dev-password",
            }));
        response.EnsureSuccessStatusCode();

        var accessToken = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var claims = JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;

        Assert.Equal(JsonValueKind.True, claims.GetProperty("email_verified").ValueKind);
    }

    [Fact]
    public async Task AddsTheEmailVerifiedMapperOnceAcrossRepeatedRuns()
    {
        var scopeId = await EmailScopeIdAsync(scenario.Admin);

        Assert.Single(await EmailVerifiedMappersAsync(scenario.Admin, scopeId));
    }

    private static async Task<string> EmailScopeIdAsync(KeycloakAdminApi admin)
    {
        var scopes = await admin.GetArrayAsync("client-scopes");
        return scopes.Single(scope => scope.GetProperty("name").GetString() == "email").GetProperty("id").GetString()!;
    }

    private static async Task<IReadOnlyList<JsonElement>> EmailVerifiedMappersAsync(KeycloakAdminApi admin, string scopeId)
    {
        var mappers = await admin.GetArrayAsync($"client-scopes/{scopeId}/protocol-mappers/models");
        return mappers
            .Where(mapper => mapper.TryGetProperty("config", out var config)
                && config.TryGetProperty("claim.name", out var claim)
                && claim.GetString() == "email_verified")
            .ToList();
    }

    private async Task<string[]> RequiredActionsAsync(string email)
    {
        var user = await scenario.Admin.GetObjectAsync($"users/{await UserIdByEmailAsync(email)}");
        return user.GetProperty("requiredActions").EnumerateArray().Select(a => a.GetString()!).Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<(string Alias, string UserId)[]> FederatedIdentitiesAsync(string userId)
    {
        var links = await scenario.Admin.GetArrayAsync($"users/{userId}/federated-identity");
        return links.Select(l => (l.GetProperty("identityProvider").GetString()!, l.GetProperty("userId").GetString()!)).ToArray();
    }

    private async Task<string> UserIdByEmailAsync(string email)
    {
        var matches = await scenario.Admin.GetArrayAsync($"users?email={Uri.EscapeDataString(email)}&exact=true");
        return Assert.Single(matches).GetProperty("id").GetString()!;
    }

    private async Task<string> UserIdByUsernameAsync(string username)
    {
        var matches = await scenario.Admin.GetArrayAsync($"users?username={Uri.EscapeDataString(username)}&exact=true");
        return Assert.Single(matches).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Seeds profiles and identities, creates self-registered Keycloak users that share an email
    /// or a username with legacy accounts, and runs the import twice, once for every test in the
    /// class. The import reads users and creates them with the command's default page and chunk
    /// sizes unless a derived scenario sets its own.
    /// </summary>
    public class Scenario : IAsyncLifetime
    {
        private readonly (int UserPageSize, int ImportChunkSize)? _sizes;

        public Scenario()
        {
        }

        protected Scenario(int userPageSize, int importChunkSize)
        {
            _sizes = (userPageSize, importChunkSize);
        }

        public KeycloakImportFixture Fixture { get; } = new();

        public KeycloakAdminApi Admin { get; private set; } = null!;

        public Dictionary<string, long> ProfileIds { get; } = [];

        public string PreexistingGraceId { get; private set; } = string.Empty;

        public string PreexistingUnverifiedJudyId { get; private set; } = string.Empty;

        /// <summary>
        /// A self-registered user whose username is Victor's legacy email while its own email
        /// differs.
        /// </summary>
        public string PreexistingSquatterId { get; private set; } = string.Empty;

        /// <summary>
        /// A self-registered user whose username is the one the import plans for Heidi's
        /// email-less facebook account.
        /// </summary>
        public string PreexistingEmaillessSquatterId { get; private set; } = string.Empty;

        public KeycloakImportSummary FirstRun { get; private set; } = null!;

        public KeycloakImportSummary SecondRun { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            await Fixture.InitializeAsync();
            Admin = new KeycloakAdminApi(Fixture.KeycloakUrl);

            await SeedProfilesAsync();
            await RemoveEmailVerifiedMapperAsync();

            PreexistingGraceId = await CreateUserAsync("grace-self", "grace@example.test", emailVerified: true);
            PreexistingUnverifiedJudyId = await CreateUserAsync("judy-self", "judy@example.test", emailVerified: false);
            PreexistingSquatterId = await CreateUserAsync("victor@example.test", "squatter@example.test", emailVerified: true);
            PreexistingEmaillessSquatterId = await CreateUserAsync("facebook-fb-heidi", "heidi-squatter@example.test", emailVerified: true);

            var settings = new KeycloakAdminSettings(
                Fixture.KeycloakUrl, KeycloakImportFixture.Realm, KeycloakImportFixture.AdminUser, KeycloakImportFixture.AdminPassword);

            FirstRun = await ImportAsync(settings);
            SecondRun = await ImportAsync(settings);
        }

        public async Task DisposeAsync()
        {
            Admin?.Dispose();
            await Fixture.DisposeAsync();
        }

        private Task<KeycloakImportSummary> ImportAsync(KeycloakAdminSettings settings) => _sizes is { } sizes
            ? KeycloakAccountImportCommand.ImportAsync(
                Fixture.LegacyConnectionString, Fixture.TargetConnectionString, Fixture.Auth0ExportPath, settings,
                sizes.UserPageSize, sizes.ImportChunkSize, CancellationToken.None)
            : KeycloakAccountImportCommand.ImportAsync(
                Fixture.LegacyConnectionString, Fixture.TargetConnectionString, Fixture.Auth0ExportPath, settings, CancellationToken.None);

        // The realm file maps email_verified; removing the mapper leaves a realm like one imported
        // from an older realm file, which the import has to repair.
        private async Task RemoveEmailVerifiedMapperAsync()
        {
            var scopeId = await EmailScopeIdAsync(Admin);
            foreach (var mapper in await EmailVerifiedMappersAsync(Admin, scopeId))
            {
                using var deleted = await Admin.DeleteAsync($"client-scopes/{scopeId}/protocol-mappers/models/{mapper.GetProperty("id").GetString()}");
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            }

            Assert.Empty(await EmailVerifiedMappersAsync(Admin, scopeId));
        }

        private async Task<string> CreateUserAsync(string username, string email, bool emailVerified)
        {
            using var created = await Admin.PostAsync("users", new { username, email, emailVerified, enabled = true });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return created.Headers.Location!.Segments[^1];
        }

        private async Task SeedProfilesAsync()
        {
            await using var db = Fixture.CreateTargetContext();
            foreach (var username in new[] { "alice", "carol", "dave", "erin", "grace", "heidi", "ivy", "ivy-other", "judy", "victor" })
            {
                var profile = new Profile { Username = username, Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow };
                db.Profiles.Add(profile);
                await db.SaveChangesAsync();
                ProfileIds[username] = profile.Id;
            }

            AddIdentity(db, "alice", "email", "alice@example.test");
            AddIdentity(db, "alice", "auth0", "auth0|pw-alice");
            AddIdentity(db, "carol", "auth0", "google-oauth2|g-carol");
            AddIdentity(db, "dave", "auth0", "facebook|fb-dave");
            AddIdentity(db, "erin", "auth0", "facebook|fb-erin");
            AddIdentity(db, "heidi", "auth0", "facebook|fb-heidi");
            AddIdentity(db, "judy", "email", "judy@example.test");
            AddIdentity(db, "victor", "email", "victor@example.test");
            AddIdentity(db, "grace", "email", "grace@example.test");
            AddIdentity(db, "ivy", "email", "ivy@example.test");
            AddIdentity(db, "ivy-other", "oidc", Uuidv5.Create(LegacyAccountPlanner.NamespaceId, "ivy@example.test").ToString());
            await db.SaveChangesAsync();
        }

        private void AddIdentity(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, string profile, string provider, string subject) =>
            db.ProfileIdentities.Add(new ProfileIdentity
            {
                ProfileId = ProfileIds[profile],
                Provider = provider,
                Subject = subject,
                CreatedAt = DateTimeOffset.UtcNow,
            });
    }

    /// <summary>
    /// Minimal Keycloak admin REST access for assertions, authenticated as the master realm admin
    /// with a fresh token per request.
    /// </summary>
    public sealed class KeycloakAdminApi(Uri baseUrl) : IDisposable
    {
        private readonly HttpClient _http = new();

        public async Task<IReadOnlyList<JsonElement>> GetArrayAsync(string realmPath)
        {
            using var response = await SendAsync(HttpMethod.Get, realmPath, null);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.EnumerateArray().ToList();
        }

        public async Task<JsonElement> GetObjectAsync(string realmPath)
        {
            using var response = await SendAsync(HttpMethod.Get, realmPath, null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public Task<HttpResponseMessage> PostAsync(string realmPath, object body) => SendAsync(HttpMethod.Post, realmPath, body);

        public Task<HttpResponseMessage> PutAsync(string realmPath, object body) => SendAsync(HttpMethod.Put, realmPath, body);

        public Task<HttpResponseMessage> DeleteAsync(string realmPath) => SendAsync(HttpMethod.Delete, realmPath, null);

        public void Dispose() => _http.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string realmPath, object? body)
        {
            var request = new HttpRequestMessage(method, new Uri(baseUrl, $"admin/realms/{KeycloakImportFixture.Realm}/{realmPath}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await _http.SendAsync(request);
        }

        private async Task<string> TokenAsync()
        {
            using var response = await _http.PostAsync(
                new Uri(baseUrl, "realms/master/protocol/openid-connect/token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "admin-cli",
                    ["username"] = KeycloakImportFixture.AdminUser,
                    ["password"] = KeycloakImportFixture.AdminPassword,
                }));
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("access_token").GetString()!;
        }
    }
}
