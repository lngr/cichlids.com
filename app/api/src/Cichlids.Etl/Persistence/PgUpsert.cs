using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Executes an <c>INSERT ... ON CONFLICT DO UPDATE</c> against a target row keyed by its legacy
/// id, the idempotency pattern every parent table in the ETL upserts through: a repeated run with
/// the same legacy row updates the existing target row instead of duplicating it.
/// </summary>
public static class PgUpsert
{
    // Every migrated table records a creation moment derived from the legacy row. That moment is
    // a property of the row's first insert, not of the ETL run that happens to touch it later, so
    // a repeated run must never overwrite it: created_at is always part of the INSERT column list
    // (a step supplies its computed value there) but never part of the DO UPDATE SET clause.
    private const string CreatedAtColumn = "created_at";

    /// <summary>
    /// Upserts one row and reports whether it was inserted or updated via the
    /// <c>xmax = 0</c> trick (a freshly inserted row has no prior transaction id in <c>xmax</c>).
    /// </summary>
    public static async Task<(long Id, bool Inserted)> UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string conflictColumn,
        IReadOnlyList<(string Column, object? Value)> values,
        CancellationToken cancellationToken)
    {
        var insertColumns = string.Join(", ", values.Select(v => v.Column));
        var insertParams = string.Join(", ", values.Select((_, i) => $"@p{i}"));
        var updateSet = string.Join(
            ", ",
            values
                .Where(v => v.Column != conflictColumn && v.Column != CreatedAtColumn)
                .Select(v => $"{v.Column} = EXCLUDED.{v.Column}"));

        var sql = $"""
            INSERT INTO {table} ({insertColumns})
            VALUES ({insertParams})
            ON CONFLICT ({conflictColumn}) DO UPDATE SET {updateSet}
            RETURNING id, (xmax = 0) AS inserted
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        for (var i = 0; i < values.Count; i++)
        {
            command.Parameters.AddWithValue($"p{i}", values[i].Value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt64(0), reader.GetBoolean(1));
    }
}
