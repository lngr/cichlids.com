// Story: task-3.30
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Cichlids.Etl.Identity;
using Cichlids.Etl.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The reset command against a Postgres and a Keycloak container of its own: in Development and
/// Staging it empties the database and deletes every realm user without the realm role admin or tester;
/// elsewhere, and when Keycloak cannot be reached, it changes nothing.
/// </summary>
public sealed class ResetCommandTests(ResetFixture fixture) : IClassFixture<ResetFixture>
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task EmptiesTheDatabaseAndKeepsOnlyTheRealmAdminsAndTesters(string environmentName)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await TargetDatabaseResetTests.SeedSpeciesAsync(connectionString);
        using var admin = await fixture.CreateAdminApiAsync();
        var plainUserId = await admin.CreateUserAsync($"plain-{Guid.NewGuid():N}");
        var adminUserId = await admin.CreateUserAsync($"admin-{Guid.NewGuid():N}");
        await admin.GrantRealmRoleAsync(adminUserId, "admin");
        var testerUserId = await admin.CreateUserAsync($"tester-{Guid.NewGuid():N}");
        await admin.GrantRealmRoleAsync(testerUserId, "tester");
        await admin.SetRegistrationAllowedAsync(true);

        var exitCode = await ResetCommand.RunAsync(connectionString, fixture.KeycloakSettings, environmentName, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var remaining = await admin.ListUserIdsAsync();
        var kept = (await admin.ListRealmRoleUserIdsAsync("admin"))
            .Concat(await admin.ListRealmRoleUserIdsAsync("tester"))
            .ToList();
        Assert.DoesNotContain(plainUserId, remaining);
        Assert.Contains(adminUserId, remaining);
        Assert.Contains(testerUserId, remaining);
        Assert.NotEmpty(remaining);
        Assert.All(remaining, id => Assert.Contains(id, kept));
        Assert.False(await admin.GetRegistrationAllowedAsync());

        using var freshAdmin = await fixture.CreateAdminApiAsync();
        Assert.NotEmpty(await freshAdmin.ListUserIdsAsync());

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Equal(0, await db.Species.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")]
    public async Task RefusesOutsideDevelopmentAndStagingAndKeepsEveryUserAndRow(string environmentName)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await TargetDatabaseResetTests.SeedSpeciesAsync(connectionString);
        using var admin = await fixture.CreateAdminApiAsync();
        var plainUserId = await admin.CreateUserAsync($"plain-{Guid.NewGuid():N}");
        var before = await admin.ListUserIdsAsync();

        var exitCode = await ResetCommand.RunAsync(connectionString, fixture.KeycloakSettings, environmentName, CancellationToken.None);

        Assert.Equal(1, exitCode);
        var after = await admin.ListUserIdsAsync();
        Assert.Equal(before.Order(), after.Order());
        Assert.Contains(plainUserId, after);

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Equal(1, await db.Species.CountAsync());
    }

    [Fact]
    public async Task DropsNoSchemaWhenKeycloakIsUnreachable()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await TargetDatabaseResetTests.SeedSpeciesAsync(connectionString);
        var unreachable = fixture.KeycloakSettings with { BaseUrl = new Uri($"http://127.0.0.1:{ClosedPort()}") };

        var exitCode = await ResetCommand.RunAsync(connectionString, unreachable, "Development", CancellationToken.None);

        Assert.Equal(1, exitCode);
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Equal(1, await db.Species.CountAsync());
    }

    [Fact]
    public async Task ResetsTheDatabaseWhenTheRealmIsMissing()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await TargetDatabaseResetTests.SeedSpeciesAsync(connectionString);
        var missingRealm = fixture.KeycloakSettings with { Realm = "missing-realm" };

        var exitCode = await ResetCommand.RunAsync(connectionString, missingRealm, "Development", CancellationToken.None);

        Assert.Equal(0, exitCode);
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Equal(0, await db.Species.CountAsync());
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>
/// A Postgres container whose databases the reset tests create one per test, and a Keycloak
/// container that imports the local stack's cichlids realm on startup.
/// </summary>
public sealed class ResetFixture : IAsyncLifetime
{
    private const string Realm = "cichlids";
    private const string AdminUser = "admin";
    private const string AdminPassword = "admin";

    private readonly PostgreSqlContainer _target = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.6.4")
        .WithUsername(AdminUser)
        .WithPassword(AdminPassword)
        .WithRealm(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cichlids-realm.json"))
        .Build();

    public KeycloakAdminSettings KeycloakSettings => new(new Uri(_keycloak.GetBaseAddress()), Realm, AdminUser, AdminPassword);

    public async Task InitializeAsync() => await Task.WhenAll(_target.StartAsync(), _keycloak.StartAsync());

    public async Task DisposeAsync()
    {
        await _target.DisposeAsync();
        await _keycloak.DisposeAsync();
    }

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_target.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_target.GetConnectionString()) { Database = name }.ConnectionString;
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public async Task<RealmAdminApi> CreateAdminApiAsync()
    {
        var baseUrl = _keycloak.GetBaseAddress().TrimEnd('/');
        var http = new HttpClient();
        using var tokenResponse = await http.PostAsync(
            $"{baseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = AdminUser,
                ["password"] = AdminPassword,
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        return new RealmAdminApi(http, $"{baseUrl}/admin/realms/{Realm}");
    }
}

/// <summary>
/// The few Keycloak admin calls the reset tests use to arrange and inspect the realm.
/// </summary>
public sealed class RealmAdminApi(HttpClient http, string realmUrl) : IDisposable
{
    public async Task<string> CreateUserAsync(string username)
    {
        using var response = await http.PostAsJsonAsync(
            $"{realmUrl}/users", new { username, enabled = true, email = $"{username}@reset.test", emailVerified = true });
        response.EnsureSuccessStatusCode();
        return response.Headers.Location!.Segments[^1];
    }

    public async Task GrantRealmRoleAsync(string userId, string role)
    {
        var representation = await http.GetFromJsonAsync<JsonElement>($"{realmUrl}/roles/{role}");
        using var response = await http.PostAsJsonAsync($"{realmUrl}/users/{userId}/role-mappings/realm", new[] { representation });
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> ListUserIdsAsync()
    {
        var users = await http.GetFromJsonAsync<List<JsonElement>>($"{realmUrl}/users?briefRepresentation=true&max=1000");
        return users!.Select(user => user.GetProperty("id").GetString()!).ToList();
    }

    public async Task<IReadOnlyList<string>> ListRealmRoleUserIdsAsync(string role)
    {
        var users = await http.GetFromJsonAsync<List<JsonElement>>($"{realmUrl}/roles/{role}/users?max=1000");
        return users!.Select(user => user.GetProperty("id").GetString()!).ToList();
    }

    public async Task SetRegistrationAllowedAsync(bool allowed)
    {
        using var response = await http.PutAsJsonAsync(realmUrl, new { registrationAllowed = allowed });
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> GetRegistrationAllowedAsync()
    {
        var realm = await http.GetFromJsonAsync<JsonElement>(realmUrl);
        return realm.GetProperty("registrationAllowed").GetBoolean();
    }

    public void Dispose() => http.Dispose();
}
