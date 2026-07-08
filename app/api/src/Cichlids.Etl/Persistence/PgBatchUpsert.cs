using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Executes one <c>INSERT ... ON CONFLICT DO UPDATE</c> statement over many rows at once, the
/// batched counterpart to <see cref="PgUpsert"/> for tables large enough (hundreds of thousands
/// of legacy rows) that a round trip per row would dominate a step's run time.
/// </summary>
public static class PgBatchUpsert
{
    // See PgUpsert: created_at is a property of a row's first insert and must survive every later
    // upsert of that row unchanged, so it is never part of the DO UPDATE SET clause. post's rating
    // aggregate columns (rating_average, rating_count) follow the same rule for a different reason:
    // once a row exists, they are owned by CommentMigrationStep's recompute pass (derived from the
    // rating table), and a later run of this upsert must not clobber them back to the picture's
    // legacy denormalized value.
    private static readonly HashSet<string> NeverUpdatedColumns = new(StringComparer.Ordinal)
    {
        "created_at", "rating_average", "rating_count",
    };

    /// <summary>
    /// Upserts a batch of rows and reports each one's id and whether it was inserted or updated,
    /// keyed by the value of <paramref name="conflictColumn"/> in each row rather than by
    /// position: a multi-row <c>INSERT ... RETURNING</c> is not guaranteed by the SQL standard to
    /// preserve the input order of its <c>VALUES</c> list, so the conflict column (expected to be
    /// the legacy id every row in the batch upserts on, hence unique within the batch) is
    /// round-tripped through <c>RETURNING</c> instead and used to match results back to rows.
    /// </summary>
    public static async Task<Dictionary<int, (long Id, bool Inserted)>> UpsertBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string conflictColumn,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var conflictColumnIndex = columns.ToList().IndexOf(conflictColumn);
        if (conflictColumnIndex < 0)
        {
            throw new ArgumentException(
                $"conflictColumn '{conflictColumn}' must be one of the supplied columns.", nameof(conflictColumn));
        }

        var insertColumns = string.Join(", ", columns);
        var updateSet = string.Join(
            ", ",
            columns
                .Where(c => c != conflictColumn && !NeverUpdatedColumns.Contains(c))
                .Select(c => $"{c} = EXCLUDED.{c}"));

        var valuesSql = new List<string>(rows.Count);
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };

        var paramIndex = 0;
        foreach (var row in rows)
        {
            var placeholders = new List<string>(columns.Count);
            foreach (var value in row)
            {
                var name = $"p{paramIndex}";
                placeholders.Add($"@{name}");
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                paramIndex++;
            }

            valuesSql.Add($"({string.Join(", ", placeholders)})");
        }

        command.CommandText = $"""
            INSERT INTO {table} ({insertColumns})
            VALUES {string.Join(", ", valuesSql)}
            ON CONFLICT ({conflictColumn}) DO UPDATE SET {updateSet}
            RETURNING {conflictColumn}, id, (xmax = 0) AS inserted
            """;

        var results = new Dictionary<int, (long Id, bool Inserted)>(rows.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results[reader.GetInt32(0)] = (reader.GetInt64(1), reader.GetBoolean(2));
        }

        return results;
    }
}
