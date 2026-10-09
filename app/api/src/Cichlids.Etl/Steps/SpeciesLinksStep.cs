using Cichlids.Etl.Runtime;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy picture-species links (user_cichlids_species_pictures_mm) into post_species.
/// Runs after PictureMigrationStep, whose post.legacy_id rows resolve each link's picture
/// reference to its migrated post, and after SpeciesCatalogStep, whose species.legacy_id rows
/// resolve its species reference. Owns the whole of post_species: every run recomputes the full
/// set from the legacy source and replaces the target table wholesale, rather than touching one
/// post at a time, since a delete-and-insert round trip per post over a source relation with well
/// over a hundred thousand rows would dominate the step's run time.
/// </summary>
public sealed class SpeciesLinksStep : IEtlStep
{
    private const int ChunkSize = 10000;

    public string Name => "species-links";

    public int Order => 45;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var postIdByLegacyId = await LoadLegacyIdMapAsync(context, "post", cancellationToken);
        var speciesIdByLegacyId = await LoadLegacyIdMapAsync(context, "species", cancellationToken);

        const string sql = """
            SELECT uid_local, uid_foreign, sorting
            FROM user_cichlids_species_pictures_mm
            ORDER BY uid_local, sorting, uid_foreign
            """;

        // Rows are read in ascending sorting order within each uid_local, so the first occurrence
        // of a (post, species) pair this loop keeps is always the one with the smallest legacy
        // sorting value; every later occurrence of the same pair is a genuine duplicate to skip.
        var seenPairs = new HashSet<(int PostLegacyId, int SpeciesLegacyId)>();
        var nextSortByPostLegacyId = new Dictionary<int, int>();
        var rows = new List<IReadOnlyList<object?>>();

        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stats.AddRead();

                var postLegacyId = reader.GetInt32(0);
                var speciesLegacyId = reader.GetInt32(1);

                if (speciesLegacyId == 0)
                {
                    stats.AddSkip("species_link_species_zero");
                    continue;
                }

                if (!postIdByLegacyId.TryGetValue(postLegacyId, out var postId))
                {
                    stats.AddSkip("species_link_post_not_migrated");
                    continue;
                }

                if (!speciesIdByLegacyId.TryGetValue(speciesLegacyId, out var speciesId))
                {
                    stats.AddSkip("species_link_species_not_migrated");
                    continue;
                }

                if (!seenPairs.Add((postLegacyId, speciesLegacyId)))
                {
                    stats.AddSkip("species_link_duplicate");
                    continue;
                }

                var sort = nextSortByPostLegacyId.GetValueOrDefault(postLegacyId);
                nextSortByPostLegacyId[postLegacyId] = sort + 1;

                rows.Add(new object?[] { postId, speciesId, sort });
            }
        }

        await using (var delete = new NpgsqlCommand("DELETE FROM post_species", context.Target, context.Transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await BulkInsertAsync(context, rows, cancellationToken);
        stats.AddInserted(rows.Count);
    }

    // Chunked so one statement never exceeds the PostgreSQL protocol limit of 65535 bind
    // parameters; 10000 rows of 3 columns stays well below it.
    private static async Task BulkInsertAsync(
        EtlContext context, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < rows.Count; offset += ChunkSize)
        {
            var chunk = rows.Skip(offset).Take(ChunkSize).ToList();
            var valuesSql = new List<string>(chunk.Count);
            await using var insert = new NpgsqlCommand { Connection = context.Target, Transaction = context.Transaction };

            var paramIndex = 0;
            foreach (var row in chunk)
            {
                var placeholders = new List<string>(row.Count);
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
                $"INSERT INTO post_species (post_id, species_id, sort) VALUES {string.Join(", ", valuesSql)}";
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Dictionary<int, long>> LoadLegacyIdMapAsync(
        EtlContext context, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT legacy_id, id FROM {table} WHERE legacy_id IS NOT NULL", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetInt32(0)] = reader.GetInt64(1);
        }

        return result;
    }
}
