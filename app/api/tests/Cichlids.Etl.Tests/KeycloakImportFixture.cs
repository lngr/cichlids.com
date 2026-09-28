using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Testcontainers.Keycloak;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts a legacy MySQL container with the fixture schema plus the fe_users and fe_users_auth0
/// rows of the Keycloak import scenario, a target Postgres container with the EF Core migrations
/// applied, and a Keycloak container that imports the local stack's cichlids realm on startup.
/// The containers belong to the Keycloak import tests alone, because the import reads every
/// fe_users_auth0 row and every profile identity without a test-specific filter.
/// </summary>
public sealed class KeycloakImportFixture : IAsyncLifetime
{
    public const string Realm = "cichlids";
    public const string AdminUser = "admin";
    public const string AdminPassword = "admin";

    private readonly MySqlContainer _legacy = new MySqlBuilder("mysql:5.7")
        .WithDatabase("cichlids_typo3")
        .WithUsername("root")
        .WithPassword("legacy")
        .Build();

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

    // See EtlFixture: the mysql:5.7 test container's TLS setup is not reliably ready on connect.
    public string LegacyConnectionString => _legacy.GetConnectionString() + ";SslMode=None";

    public string TargetConnectionString => _target.GetConnectionString();

    public Uri KeycloakUrl => new(_keycloak.GetBaseAddress());

    public string Auth0ExportPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "keycloak-import-auth0.ndjson");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_legacy.StartAsync(), _target.StartAsync(), _keycloak.StartAsync());
        await SeedLegacyAsync();

        await using var db = CreateTargetContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _legacy.DisposeAsync();
        await _target.DisposeAsync();
        await _keycloak.DisposeAsync();
    }

    public CichlidsDbContext CreateTargetContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(TargetConnectionString)
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    private async Task SeedLegacyAsync()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var schema = await File.ReadAllTextAsync(Path.Combine(fixturesDir, "legacy-schema.sql"));
        var seed = await File.ReadAllTextAsync(Path.Combine(fixturesDir, "keycloak-import-legacy-seed.sql"));

        // See EtlFixture: the first connection after the mysql:5.7 entrypoint restart can fail, and
        // the schema script drops and recreates every table, so a retry is safe.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new MySqlConnection(LegacyConnectionString);
                await connection.OpenAsync();

                await ExecuteScriptAsync(connection, schema);
                await ExecuteScriptAsync(connection, seed);
                return;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task ExecuteScriptAsync(MySqlConnection connection, string script)
    {
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length == 0 || trimmed.Split('\n').All(line => line.TrimStart().StartsWith("--", StringComparison.Ordinal)))
            {
                continue;
            }

            await using var command = new MySqlCommand(trimmed, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
