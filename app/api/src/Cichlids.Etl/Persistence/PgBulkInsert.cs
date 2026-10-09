using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Chunked <c>INSERT ... ON CONFLICT DO NOTHING</c> over many rows at once, for tables the ETL
/// treats as an append-only archive: a row already present under its conflict key is left exactly
/// as it stands, unlike <see cref="PgBatchUpsert"/> which keeps a migrated row in sync with its
/// legacy source on every run.
/// </summary>
public static class PgBulkInsert
{
    // Keeps the parameter count for the widest current caller (legacy_comment, 11 columns) well
    // below the PostgreSQL protocol limit of 65535 bind parameters per statement.
    private const int ChunkSize = 5000;

    /// <summary>
    /// Inserts rows in chunks, skipping any row whose conflict-column values already exist, and
    /// returns the total number of rows actually inserted across all chunks. <paramref
    /// name="columnCasts"/> optionally names an explicit target type (for example <c>jsonb</c>)
    /// for a column whose value would otherwise be sent as a type PostgreSQL has no implicit
    /// conversion from, keyed by column name.
    /// </summary>
    public static async Task<int> InsertOnConflictDoNothingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? columnCasts = null)
    {
        var inserted = 0;
        for (var offset = 0; offset < rows.Count; offset += ChunkSize)
        {
            var chunk = rows.Skip(offset).Take(ChunkSize).ToList();
            inserted += await InsertChunkAsync(
                connection, transaction, table, conflictColumns, columns, chunk, columnCasts, cancellationToken);
        }

        return inserted;
    }

    private static async Task<int> InsertChunkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        IReadOnlyList<string> conflictColumns,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        IReadOnlyDictionary<string, string>? columnCasts,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var valuesSql = new List<string>(rows.Count);
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };

        var paramIndex = 0;
        foreach (var row in rows)
        {
            var placeholders = new List<string>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                var name = $"p{paramIndex}";
                var cast = columnCasts is not null && columnCasts.TryGetValue(columns[i], out var castType)
                    ? $"::{castType}"
                    : string.Empty;
                placeholders.Add($"@{name}{cast}");
                command.Parameters.AddWithValue(name, row[i] ?? DBNull.Value);
                paramIndex++;
            }

            valuesSql.Add($"({string.Join(", ", placeholders)})");
        }

        command.CommandText = $"""
            INSERT INTO {table} ({string.Join(", ", columns)})
            VALUES {string.Join(", ", valuesSql)}
            ON CONFLICT ({string.Join(", ", conflictColumns)}) DO NOTHING
            RETURNING 1
            """;

        var inserted = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            inserted++;
        }

        return inserted;
    }
}
