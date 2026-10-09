using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Replaces the child rows of one parent with a freshly computed set. Child tables (species
/// common names and links, profile identities) have no legacy id of their own to upsert on, so
/// each ETL run deletes what is currently under the parent and re-inserts the recomputed rows;
/// this is idempotent because it always converges to the same set for the same source data.
/// </summary>
public static class PgChildRows
{
    public static async Task ReplaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string parentColumn,
        long parentId,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
            $"DELETE FROM {table} WHERE {parentColumn} = @parentId", connection, transaction))
        {
            delete.Parameters.AddWithValue("parentId", parentId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (rows.Count == 0)
        {
            return;
        }

        var allColumns = new List<string>(columns.Count + 1) { parentColumn };
        allColumns.AddRange(columns);

        var valuesSql = new List<string>(rows.Count);
        await using var insert = new NpgsqlCommand { Connection = connection, Transaction = transaction };

        var paramIndex = 0;
        foreach (var row in rows)
        {
            var placeholders = new List<string>(allColumns.Count) { $"@p{paramIndex}" };
            insert.Parameters.AddWithValue($"p{paramIndex}", parentId);
            paramIndex++;

            foreach (var value in row)
            {
                var name = $"p{paramIndex}";
                placeholders.Add($"@{name}");
                insert.Parameters.AddWithValue(name, value ?? DBNull.Value);
                paramIndex++;
            }

            valuesSql.Add($"({string.Join(", ", placeholders)})");
        }

        insert.CommandText =
            $"INSERT INTO {table} ({string.Join(", ", allColumns)}) VALUES {string.Join(", ", valuesSql)}";

        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
