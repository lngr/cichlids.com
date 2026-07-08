using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts one legacy MySQL container seeded with the fixture schema and edge-case rows the step
/// tests assert against, and one target Postgres container with the EF Core migrations applied,
/// shared across the whole test collection so every test runs the steps against the same seed.
/// </summary>
public sealed class EtlFixture : IAsyncLifetime
{
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

    // MySqlConnector defaults to SslMode=Preferred, but the mysql:5.7 test container's
    // self-signed certificate setup is not consistently ready by the time the fixture connects.
    // The legacy data never crosses a network boundary worth encrypting in the first place.
    public string LegacyConnectionString => _legacy.GetConnectionString() + ";SslMode=None";

    public string TargetConnectionString => _target.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_legacy.StartAsync(), _target.StartAsync());
        await SeedLegacyAsync();

        await using var db = CreateTargetContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _legacy.DisposeAsync();
        await _target.DisposeAsync();
    }

    /// <summary>
    /// Opens a fresh context against the target database, independent of any ETL run's own
    /// transaction, for assertions after a run has committed.
    /// </summary>
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
        var seed = await File.ReadAllTextAsync(Path.Combine(fixturesDir, "legacy-seed.sql"));

        // The mysql:5.7 entrypoint restarts the server once after its first boot to apply the
        // initialized system tables; a connection opened in that window can read a corrupted
        // reply on the first command. The schema script drops and recreates every table, so a
        // retry with a fresh connection is safe.
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
            if (trimmed.Length == 0)
            {
                continue;
            }

            try
            {
                await using var command = new MySqlCommand(trimmed, connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex)
            {
                throw new InvalidOperationException($"Failed statement:\n{trimmed}", ex);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class EtlCollection : ICollectionFixture<EtlFixture>
{
    public const string Name = "Etl";
}
