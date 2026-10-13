using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Replaces the child rows of one parent with a freshly computed set. Child tables (species
/// common names and links, profile identities) have no legacy id of their own to upsert on, so
/// the helper deletes the rows under the parent and inserts the computed ones. For the same
/// source data the parent ends with the same set, whether it had child rows before or not.
/// With a scope, the delete covers only the rows of the parent whose scope column holds one of
/// the allowed values, and every inserted row has to lie inside that scope, so rows written by
/// other components stay untouched.
/// </summary>
public static class PgChildRows
{
    /// <summary>
    /// Restricts a replace to the rows whose column holds one of the allowed values.
    /// </summary>
    public sealed record Scope(string Column, IReadOnlyList<string> AllowedValues);

    public static async Task ReplaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string parentColumn,
        long parentId,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken,
        Scope? scope = null)
    {
        var scopeIndex = -1;
        if (scope is not null)
        {
            scopeIndex = columns.ToList().IndexOf(scope.Column);
            if (scopeIndex < 0)
            {
                throw new ArgumentException($"The scope column {scope.Column} is not one of the inserted columns.", nameof(scope));
            }

            foreach (var row in rows)
            {
                if (row[scopeIndex] is not string value || !scope.AllowedValues.Contains(value))
                {
                    throw new InvalidOperationException(
                        $"A row for {table} has the value '{row[scopeIndex]}' in {scope.Column}, which is outside the replace scope.");
                }
            }
        }

        var deleteSql = $"DELETE FROM {table} WHERE {parentColumn} = @parentId";
        if (scope is not null)
        {
            deleteSql += $" AND {scope.Column} = ANY(@scopeValues)";
        }

        await using (var delete = new NpgsqlCommand(deleteSql, connection, transaction))
        {
            delete.Parameters.AddWithValue("parentId", parentId);
            if (scope is not null)
            {
                delete.Parameters.AddWithValue("scopeValues", scope.AllowedValues.ToArray());
            }

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
