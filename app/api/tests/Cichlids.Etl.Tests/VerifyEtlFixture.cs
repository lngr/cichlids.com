using Amazon.Runtime;
using Amazon.S3;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Starts a legacy MySQL container and a target Postgres container of its own, seeded with
/// verify-legacy-seed.sql: a small dataset every migration step can process end to end with no
/// row left dangling. Kept separate from EtlFixture's shared containers (which several test
/// classes seed with their own Postgres-only prerequisite rows that have no legacy counterpart) so
/// a full-pipeline run here converges to a target with zero verification mismatches by
/// construction, not by coincidence of test execution order. The full pipeline runs once per
/// fixture into a database of its own; tests change that database only inside transactions they
/// roll back.
/// </summary>
public sealed class VerifyEtlFixture : IAsyncLifetime
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

    private const string ObjectStoreAccessKey = "cichlids";
    private const string ObjectStoreSecretKey = "cichlids-dev-secret"; // gitleaks:allow
    private const string ObjectStoreBucket = "cichlids-media-verify-test";
    private const int RustfsPort = 9000;

    private readonly IContainer _rustfs = new ContainerBuilder("rustfs/rustfs:1.0.0-beta.8")
        .WithEnvironment("RUSTFS_ACCESS_KEY", ObjectStoreAccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", ObjectStoreSecretKey)
        .WithPortBinding(RustfsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(RustfsPort))
        .Build();

    // MySqlConnector defaults to SslMode=Preferred, but the mysql:5.7 test container's
    // self-signed certificate setup is not consistently ready by the time the fixture connects.
    public string LegacyConnectionString => _legacy.GetConnectionString() + ";SslMode=None";

    public string TargetConnectionString => _target.GetConnectionString();

    public IObjectStore ObjectStore { get; private set; } = null!;

    /// <summary>
    /// Connection string of the database the full pipeline ran into once for this fixture.
    /// </summary>
    public string PipelineConnectionString { get; private set; } = null!;

    /// <summary>
    /// Exit code of the full pipeline run: 0 when its verification passed.
    /// </summary>
    public int PipelineExitCode { get; private set; }

    /// <summary>
    /// The verification report of the full pipeline run.
    /// </summary>
    public VerificationReport PipelineReport { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_legacy.StartAsync(), _target.StartAsync(), _rustfs.StartAsync());
        await SeedLegacyAsync();

        await using (var db = CreateTargetContext())
        {
            await db.Database.MigrateAsync();
        }

        ObjectStore = await CreateObjectStoreAsync();

        PipelineConnectionString = await CreateEmptyTargetDatabaseAsync();
        await using var context = await EtlContext.CreateAsync(
            LegacyConnectionString, PipelineConnectionString, CancellationToken.None, ObjectStore);
        PipelineExitCode = await EtlRunner.RunAllAsync(
            context, EtlStepRegistry.WriteSteps, EtlStepRegistry.Verify, afterGuard: null, CancellationToken.None);
        PipelineReport = context.VerificationReport!;
    }

    /// <summary>
    /// Runs the verify step against the pipeline database inside a transaction, after mutate (when
    /// given) has changed the target in that same transaction, and rolls the transaction back.
    /// Returns the report of that verification.
    /// </summary>
    public async Task<VerificationReport> VerifyInRolledBackTransactionAsync(Func<EtlContext, Task>? mutate = null)
    {
        await using var context = await EtlContext.CreateAsync(
            LegacyConnectionString, PipelineConnectionString, CancellationToken.None, ObjectStore);
        await using var transaction = await context.Target.BeginTransactionAsync();
        context.Db.Database.UseTransaction(transaction);
        context.Transaction = transaction;

        try
        {
            if (mutate is not null)
            {
                await mutate(context);
            }

            await new VerifyStep().RunAsync(context, CancellationToken.None);
            return context.VerificationReport!;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// Executes one SQL statement in the context's current transaction.
    /// </summary>
    public static async Task ExecuteAsync(EtlContext context, string sql)
    {
        await using var command = new NpgsqlCommand(sql, context.Target, context.Transaction);
        await command.ExecuteNonQueryAsync();
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

    public CichlidsDbContext CreateTargetContext() => CreateTargetContext(TargetConnectionString);

    public static CichlidsDbContext CreateTargetContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new CichlidsDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Creates a database of its own in the target container, applies the migrations up to and
    /// including the given one (all of them when none is given), and returns its connection
    /// string. Tests that need an empty target use it, since the fixture's own target database is
    /// shared by every test of the collection.
    /// </summary>
    public async Task<string> CreateEmptyTargetDatabaseAsync(string? lastMigration = null)
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(TargetConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(TargetConnectionString) { Database = name }.ConnectionString;
        await using var db = CreateTargetContext(connectionString);
        if (lastMigration is null)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.GetService<IMigrator>().MigrateAsync(lastMigration);
        }

        return connectionString;
    }

    private async Task SeedLegacyAsync()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var schema = await File.ReadAllTextAsync(Path.Combine(fixturesDir, "legacy-schema.sql"));
        var seed = await File.ReadAllTextAsync(Path.Combine(fixturesDir, "verify-legacy-seed.sql"));

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
public sealed class VerifyEtlCollection : ICollectionFixture<VerifyEtlFixture>
{
    public const string Name = "EtlVerify";
}
