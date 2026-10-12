using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// Checks that the target database is ready for the full ETL run: no other run guards it, every
/// migration is applied, and no base table of the schemas the EF Core model maps holds a row
/// (the migration history table aside). The check takes a transaction-scoped advisory lock, so of
/// two runs started at the same time only one passes, and that run holds the lock until its
/// transaction ends.
/// </summary>
public static class EmptyTargetGuard
{
    /// <summary>
    /// The key of the advisory lock the full run holds for its transaction.
    /// </summary>
    public const long AdvisoryLockKey = 0x63696368_6C696473;

    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Runs the check inside the current transaction of the given context and throws an
    /// EmptyTargetException naming the reason when the target is not ready. Throws an
    /// InvalidOperationException when the context has no current transaction.
    /// </summary>
    public static async Task EnsureEmptyAsync(CichlidsDbContext db, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("The empty-target check needs an open transaction to hold its lock.");
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not true)
            {
                throw new EmptyTargetException(
                    "The target database is in use by another ETL run; this run stops before its first write.", []);
            }
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            throw new EmptyTargetException(
                $"The target database has pending migrations: {string.Join(", ", pending)}. "
                + "Apply them with 'migrate' (or reset the database in Development and Staging); this run stops before its first write.",
                []);
        }

        var tablesWithRows = await FindTablesWithRowsAsync(db, connection, transaction, cancellationToken);
        if (tablesWithRows.Count > 0)
        {
            throw new EmptyTargetException(
                $"The target database is not empty; the full run writes only into an empty target. Tables with rows: {string.Join(", ", tablesWithRows)}. "
                + "Reset the database in Development and Staging, or point the run at an empty database; this run stops before its first write.",
                tablesWithRows);
        }
    }

    private static async Task<IReadOnlyList<string>> FindTablesWithRowsAsync(
        CichlidsDbContext db, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var schemas = db.Model.GetEntityTypes()
            .Select(entityType => entityType.GetSchema() ?? db.Model.GetDefaultSchema() ?? "public")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var tables = new List<(string Schema, string Name, string Quoted)>();
        await using (var listCommand = new NpgsqlCommand(
            """
            SELECT table_schema, table_name, format('%I.%I', table_schema, table_name)
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE' AND table_schema = ANY(@schemas) AND table_name <> @history
            ORDER BY table_schema, table_name
            """,
            connection,
            transaction))
        {
            listCommand.Parameters.AddWithValue("schemas", schemas);
            listCommand.Parameters.AddWithValue("history", MigrationHistoryTable);
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var tablesWithRows = new List<string>();
        foreach (var table in tables)
        {
            await using var existsCommand = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {table.Quoted})", connection, transaction);
            if (await existsCommand.ExecuteScalarAsync(cancellationToken) is true)
            {
                tablesWithRows.Add($"{table.Schema}.{table.Name}");
            }
        }

        return tablesWithRows;
    }
}

/// <summary>
/// Thrown when the target database is not ready for the full ETL run. TablesWithRows lists the
/// tables that hold a row, as schema.table, and is empty for the other reasons.
/// </summary>
public sealed class EmptyTargetException(string message, IReadOnlyList<string> tablesWithRows)
    : InvalidOperationException(message)
{
    public IReadOnlyList<string> TablesWithRows { get; } = tablesWithRows;
}
