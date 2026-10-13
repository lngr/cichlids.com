using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// Starts one Postgres container for the whole test collection and applies the EF Core
/// migrations to it once, so every test in the collection runs against the same migrated
/// schema instead of paying for a fresh container and migration run each time.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("cichlids")
        .WithUsername("cichlids")
        .WithPassword("cichlids")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public CichlidsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Creates a new, empty database in the container without applying any migration, for tests
    /// that migrate step by step. Disposing the result drops the database.
    /// </summary>
    public async Task<TemporaryDatabase> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"empty_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {databaseName}");

        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
        return new TemporaryDatabase(connectionString, () => ExecuteAsync($"DROP DATABASE {databaseName} WITH (FORCE)"));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs a scalar query against the raw connection, for assertions the EF Core model does
    /// not expose (catalog views such as information_schema.tables or pg_indexes).
    /// </summary>
    public async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}

/// <summary>
/// A database of the fixture container that exists until it is disposed.
/// </summary>
public sealed class TemporaryDatabase(string connectionString, Func<Task> drop) : IAsyncDisposable
{
    public CichlidsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        await drop();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
