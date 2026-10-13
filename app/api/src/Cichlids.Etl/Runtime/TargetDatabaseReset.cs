using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Empties the target database by dropping the archive and public schemas, recreating public
/// with the PostgreSQL defaults and applying every migration. The drop waits at most ten seconds
/// for a lock, so a client that holds one (a running API, for instance) makes the reset fail with
/// a lock timeout. Runs only where the reset policy allows it.
/// </summary>
public static class TargetDatabaseReset
{
    private const string DropSql =
        """
        SET LOCAL lock_timeout = '10s';
        DROP SCHEMA IF EXISTS archive CASCADE;
        DROP SCHEMA IF EXISTS public CASCADE;
        CREATE SCHEMA public AUTHORIZATION pg_database_owner;
        GRANT USAGE ON SCHEMA public TO PUBLIC;
        """;

    public static async Task ResetAsync(
        string targetConnectionString, string? environmentName, CancellationToken cancellationToken)
    {
        ResetPolicy.EnsureAllowed(environmentName);

        var target = new NpgsqlConnectionStringBuilder(targetConnectionString);
        Console.WriteLine($"Resetting database '{target.Database}' on {target.Host}:{target.Port}...");

        var optionsBuilder = new DbContextOptionsBuilder<CichlidsDbContext>()
            .UseNpgsql(targetConnectionString)
            .UseSnakeCaseNamingConvention();
        await using var db = new CichlidsDbContext(optionsBuilder.Options);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(DropSql, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
        Console.WriteLine("Database reset and migrated.");
    }
}
