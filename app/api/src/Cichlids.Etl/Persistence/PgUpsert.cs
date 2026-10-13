using Npgsql;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Executes an INSERT ... ON CONFLICT DO UPDATE against a target row keyed by a unique column,
/// usually its legacy id. A row that exists under the key is updated in place. Within one full
/// run this lets several steps write the same row (the placeholder profiles, for instance), and a
/// single step run in Development or a step test can run over the rows of an earlier run.
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
    /// xmax = 0 trick (a freshly inserted row has no prior transaction id in xmax).
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
