using Cichlids.Etl.Runtime;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy picture-species links (user_cichlids_species_pictures_mm) into story_subject rows of type species, without a position.
/// Runs after PictureMigrationStep, whose story.legacy_id rows resolve each link's picture
/// reference to its migrated story, and after SpeciesCatalogStep, whose species.legacy_id rows
/// resolve its species reference. Owns the species subjects of all stories: every run recomputes the full
/// set from the legacy source and replaces them wholesale, rather than touching one
/// story at a time, since a delete-and-insert round trip per story over a source relation with well
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

        var storyIdByLegacyId = await LoadLegacyIdMapAsync(context, "story", cancellationToken);
        var speciesIdByLegacyId = await LoadLegacyIdMapAsync(context, "species", cancellationToken);

        const string sql = """
            SELECT uid_local, uid_foreign, sorting
            FROM user_cichlids_species_pictures_mm
            ORDER BY uid_local, sorting, uid_foreign
            """;

        // Rows are read in ascending sorting order within each uid_local, so the first occurrence
        // of a (story, species) pair this loop keeps is always the one with the smallest legacy
        // sorting value; every later occurrence of the same pair is a genuine duplicate to skip.
        var seenPairs = new HashSet<(int StoryLegacyId, int SpeciesLegacyId)>();
        var rows = new List<IReadOnlyList<object?>>();

        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stats.AddRead();

                var storyLegacyId = reader.GetInt32(0);
                var speciesLegacyId = reader.GetInt32(1);

                if (speciesLegacyId == 0)
                {
                    stats.AddSkip("species_link_species_zero");
                    continue;
                }

                if (!storyIdByLegacyId.TryGetValue(storyLegacyId, out var storyId))
                {
                    stats.AddSkip("species_link_story_not_migrated");
                    continue;
                }

                if (!speciesIdByLegacyId.TryGetValue(speciesLegacyId, out var speciesId))
                {
                    stats.AddSkip("species_link_species_not_migrated");
                    continue;
                }

                if (!seenPairs.Add((storyLegacyId, speciesLegacyId)))
                {
                    stats.AddSkip("species_link_duplicate");
                    continue;
                }

                rows.Add(new object?[] { storyId, speciesId });
            }
        }

        // Empty in the full run; a single step run in Development or a step test replaces the
        // rows of an earlier run.
        await using (var delete = new NpgsqlCommand("DELETE FROM story_subject WHERE subject_type = 'species'", context.Target, context.Transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await BulkInsertAsync(context, rows, cancellationToken);
        stats.AddInserted(rows.Count);
    }

    // Chunked so one statement never exceeds the PostgreSQL protocol limit of 65535 bind
    // parameters; 10000 rows of 2 columns stays well below it.
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
                var storyParam = $"s{paramIndex}";
                var speciesParam = $"p{paramIndex}";
                insert.Parameters.AddWithValue(storyParam, row[0]!);
                insert.Parameters.AddWithValue(speciesParam, row[1]!);
                valuesSql.Add($"(@{storyParam}, 'species', @{speciesParam})");
                paramIndex++;
            }

            insert.CommandText =
                $"INSERT INTO story_subject (story_id, subject_type, species_id) VALUES {string.Join(", ", valuesSql)}";
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
