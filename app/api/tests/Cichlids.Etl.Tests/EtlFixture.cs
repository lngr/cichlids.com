using Amazon.Runtime;
using Amazon.S3;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts one legacy MySQL container seeded with the fixture schema and edge-case rows the step
/// tests assert against, and one target Postgres container with the EF Core migrations applied,
/// shared across the whole test collection so every test runs the steps against the same seed.
/// </summary>
public sealed class EtlFixture : IAsyncLifetime
{
    // Every test class in this collection reads and writes the same target Postgres tables, so
    // two step runs from different test classes must never execute at the same time even though
    // xunit already restricts this collection to one test at a time: a run in flight here is a
    // multi-statement, multi-round-trip sequence (several MySQL reads plus several Postgres
    // upserts) that other test code must not interleave with.
    private readonly SemaphoreSlim _stepLock = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
    {
        await _stepLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            _stepLock.Release();
        }
    }

    public async Task RunExclusiveAsync(Func<Task> action)
    {
        await _stepLock.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _stepLock.Release();
        }
    }

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

    // Same image and credentials as the local stack (app/stack/compose.yaml), so
    // ForumMigrationStepTests exercises the real S3-compatible upload path, not a mock.
    private const string ObjectStoreAccessKey = "cichlids";
    private const string ObjectStoreSecretKey = "cichlids-dev-secret";
    private const string ObjectStoreBucket = "cichlids-media-test";
    private const int RustfsPort = 9000;

    private readonly IContainer _rustfs = new ContainerBuilder("rustfs/rustfs:1.0.0-beta.8")
        .WithEnvironment("RUSTFS_ACCESS_KEY", ObjectStoreAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", ObjectStoreSecretKey)
        .WithPortBinding(RustfsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(RustfsPort))
        .Build();

    // MySqlConnector defaults to SslMode=Preferred, but the mysql:5.7 test container's
    // self-signed certificate setup is not consistently ready by the time the fixture connects.
    // The legacy data never crosses a network boundary worth encrypting in the first place.
    public string LegacyConnectionString => _legacy.GetConnectionString() + ";SslMode=None";

    public string TargetConnectionString => _target.GetConnectionString();

    /// <summary>
    /// The object store ForumMigrationStep exports attachments into, backed by the rustfs
    /// container started for this fixture.
    /// </summary>
    public IObjectStore ObjectStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_legacy.StartAsync(), _target.StartAsync(), _rustfs.StartAsync());
        await SeedLegacyAsync();

        await using var db = CreateTargetContext();
        await db.Database.MigrateAsync();

        ObjectStore = await CreateObjectStoreAsync();
    }

    public async Task DisposeAsync()
    {
        await _legacy.DisposeAsync();
        await _target.DisposeAsync();
        await _rustfs.DisposeAsync();
    }

    private async Task<IObjectStore> CreateObjectStoreAsync()
    {
        var options = new S3ObjectStoreOptions
        {
            ServiceUrl = $"http://{_rustfs.Hostname}:{_rustfs.GetMappedPublicPort(RustfsPort)}",
            Region = "us-east-1",
            Bucket = ObjectStoreBucket,
            AccessKey = ObjectStoreAccessKey,
            SecretKey = ObjectStoreSecretKey,
            ForcePathStyle = true,
            PublicBaseUrl = $"http://{_rustfs.Hostname}:{_rustfs.GetMappedPublicPort(RustfsPort)}/{ObjectStoreBucket}",
        };

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = options.ForcePathStyle, AuthenticationRegion = options.Region });

        var store = new S3ObjectStore(client, Options.Create(options));
        await store.EnsureBucketAsync();
        return store;
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
