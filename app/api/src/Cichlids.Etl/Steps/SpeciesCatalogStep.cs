using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates the legacy species catalog (<c>user_cichlids_species</c> and its lookup and link
/// tables) into <c>species</c>, <c>species_common_name</c> and <c>species_link</c>. The parent
/// row upserts on <c>legacy_id</c>; the child rows are replaced under the parent on every run.
/// </summary>
public sealed class SpeciesCatalogStep : IEtlStep
{
    private static readonly ValueConverter<SpeciesBreeding, string> BreedingConverter = new SnakeCaseEnumConverter<SpeciesBreeding>();
    private static readonly ValueConverter<AggressionLevel, string> AggressionConverter = new SnakeCaseEnumConverter<AggressionLevel>();
    private static readonly ValueConverter<SpeciesDiet, string> DietConverter = new SnakeCaseEnumConverter<SpeciesDiet>();

    public string Name => "species";

    public int Order => 10;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var slugsByLegacyId = await LoadSlugsAsync(context, stats, cancellationToken);
        var commonNamesByLegacyId = await LoadCommonNamesAsync(context, cancellationToken);

        const string sql = """
            SELECT
                s.uid, s.title,
                g.title AS genus_title,
                n.title AS name_title,
                cat.title AS category_title,
                s.temp, s.ph, s.gh, s.kh, s.max_size,
                s.description, s.origin, s.habitat, s.morphs_text,
                s.breeding, s.aggro, s.inner_aggro, s.diet,
                s.links, s.link_texts
            FROM user_cichlids_species s
            LEFT JOIN user_cichlids_genus_names g ON g.uid = s.genus AND g.deleted = 0
            LEFT JOIN user_cichlids_species_names n ON n.uid = s.species AND n.deleted = 0
            LEFT JOIN user_cichlids_category cat ON cat.uid = s.category AND cat.deleted = 0
            WHERE s.deleted = 0
            ORDER BY s.uid
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stats.AddRead();

            var legacyId = reader.GetInt32("uid");
            var title = reader.GetTrimmedOrNull("title");
            var genusTitle = reader.GetTrimmedOrNull("genus_title");
            var nameTitle = reader.GetTrimmedOrNull("name_title");
            var categoryTitle = reader.GetTrimmedOrNull("category_title");

            if (genusTitle is null)
            {
                stats.AddSkip("species_missing_genus_lookup");
            }

            if (nameTitle is null)
            {
                stats.AddSkip("species_missing_species_name_lookup");
            }

            var genus = genusTitle ?? string.Empty;
            var name = nameTitle ?? string.Empty;
            var displayName = title ?? $"{genus} {name}".Trim();
            var category = categoryTitle?.ToLowerInvariant();

            var breeding = MapBreeding(reader.GetInt32("breeding"), stats);
            var aggression = MapAggression(reader.GetInt32("aggro"), stats, "aggro");
            var intraAggression = MapAggression(reader.GetInt32("inner_aggro"), stats, "inner_aggro");
            var diet = MapDiet(reader.GetInt32("diet"), stats);

            slugsByLegacyId.TryGetValue(legacyId, out var slug);

            var values = new (string, object?)[]
            {
                ("legacy_id", legacyId),
                ("genus", genus),
                ("name", name),
                ("display_name", displayName),
                ("category", category),
                ("temperature_range", reader.GetTrimmedOrNull("temp")),
                ("ph_range", reader.GetTrimmedOrNull("ph")),
                ("gh_range", reader.GetTrimmedOrNull("gh")),
                ("kh_range", reader.GetTrimmedOrNull("kh")),
                ("max_size", reader.GetTrimmedOrNull("max_size")),
                ("breeding", BreedingConverter.ConvertToProvider(breeding)),
                ("aggression", AggressionConverter.ConvertToProvider(aggression)),
                ("intra_aggression", AggressionConverter.ConvertToProvider(intraAggression)),
                ("diet", DietConverter.ConvertToProvider(diet)),
                ("morphs", reader.GetTrimmedOrNull("morphs_text")),
                ("description", reader.GetTrimmedOrNull("description")),
                ("origin", reader.GetTrimmedOrNull("origin")),
                ("habitat", reader.GetTrimmedOrNull("habitat")),
                ("slug", slug),
                ("created_at", DateTimeOffset.UtcNow),
            };

            var (speciesId, inserted) = await PgUpsert.UpsertAsync(
                context.Target, context.Transaction, "species", "legacy_id", values, cancellationToken);

            if (inserted)
            {
                stats.AddInserted();
            }
            else
            {
                stats.AddUpdated();
            }

            var commonNames = commonNamesByLegacyId.TryGetValue(legacyId, out var names) ? names : [];
            await PgChildRows.ReplaceAsync(
                context.Target,
                context.Transaction,
                "species_common_name",
                "species_id",
                speciesId,
                ["name"],
                commonNames.Select(n => (IReadOnlyList<object?>)new object?[] { n }).ToList(),
                cancellationToken);

            var links = ParseLinks(
                reader.GetStringOrEmpty("links"), reader.GetStringOrEmpty("link_texts"), stats);
            await PgChildRows.ReplaceAsync(
                context.Target,
                context.Transaction,
                "species_link",
                "species_id",
                speciesId,
                ["url", "label", "sort"],
                links.Select((l, i) => (IReadOnlyList<object?>)new object?[] { l.Url, l.Label, i }).ToList(),
                cancellationToken);
        }
    }

    private static async Task<Dictionary<int, string>> LoadSlugsAsync(
        EtlContext context, StepStatistics stats, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT value_id, uid, value_alias
            FROM tx_realurl_uniqalias
            WHERE tablename = 'user_cichlids_species'
            ORDER BY value_id, uid
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // The first row per value_id (legacy species uid) after the ORDER BY has the lowest uid,
        // i.e. the oldest alias, which is the one that wins for that species.
        var chosen = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var valueId = reader.GetInt32("value_id");
            if (!chosen.ContainsKey(valueId))
            {
                chosen[valueId] = reader.GetString("value_alias");
            }
        }

        // Two different species can still end up with the same alias text (a species was
        // reassigned an alias that another species already holds). The lower legacy id keeps the
        // plain alias; the rest get their legacy id appended to stay unique.
        var result = new Dictionary<int, string>(chosen);
        foreach (var group in chosen.GroupBy(kv => kv.Value).Where(g => g.Count() > 1))
        {
            foreach (var kv in group.OrderBy(kv => kv.Key).Skip(1))
            {
                result[kv.Key] = $"{kv.Value}-{kv.Key}";
                stats.AddWarning(
                    $"species alias '{kv.Value}' collides for legacy id {kv.Key}; suffixed to '{result[kv.Key]}'.");
            }
        }

        return result;
    }

    private static async Task<Dictionary<int, List<string>>> LoadCommonNamesAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT mm.uid_local AS species_uid, cn.title
            FROM user_cichlids_species_common_mm mm
            JOIN user_cichlids_common_name cn ON cn.uid = mm.uid_foreign AND cn.deleted = 0
            ORDER BY mm.uid_local, mm.sorting
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, List<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var speciesUid = reader.GetInt32("species_uid");
            var title = reader.GetString("title").Trim();
            if (title.Length == 0)
            {
                continue;
            }

            if (!result.TryGetValue(speciesUid, out var names))
            {
                names = [];
                result[speciesUid] = names;
            }

            if (!names.Contains(title, StringComparer.Ordinal))
            {
                names.Add(title);
            }
        }

        return result;
    }

    private static List<(string Url, string? Label)> ParseLinks(string links, string linkTexts, StepStatistics stats)
    {
        var urlLines = links.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var labelLines = linkTexts.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var result = new List<(string, string?)>();
        for (var i = 0; i < urlLines.Length; i++)
        {
            var url = urlLines[i].Trim();
            if (url.Length == 0)
            {
                stats.AddSkip("species_link_empty_url");
                continue;
            }

            var label = i < labelLines.Length ? labelLines[i].Trim() : null;
            result.Add((url, string.IsNullOrEmpty(label) ? null : label));
        }

        return result;
    }

    private static SpeciesBreeding MapBreeding(int value, StepStatistics stats) => value switch
    {
        0 => SpeciesBreeding.Unspecified,
        1 => SpeciesBreeding.Mouthbreeder,
        2 => SpeciesBreeding.CaveBreeder,
        3 => SpeciesBreeding.SubstrateBreeder,
        _ => Unknown(stats, "breeding", value, SpeciesBreeding.Unspecified),
    };

    private static AggressionLevel MapAggression(int value, StepStatistics stats, string field) => value switch
    {
        0 => AggressionLevel.Unspecified,
        1 => AggressionLevel.Low,
        2 => AggressionLevel.Moderate,
        3 => AggressionLevel.High,
        _ => Unknown(stats, field, value, AggressionLevel.Unspecified),
    };

    private static SpeciesDiet MapDiet(int value, StepStatistics stats) => value switch
    {
        0 => SpeciesDiet.Unspecified,
        1 => SpeciesDiet.Omnivore,
        2 => SpeciesDiet.Carnivore,
        3 => SpeciesDiet.Herbivore,
        4 => SpeciesDiet.Limnivore,
        _ => Unknown(stats, "diet", value, SpeciesDiet.Unspecified),
    };

    private static T Unknown<T>(StepStatistics stats, string field, int value, T fallback)
    {
        stats.AddSkip($"species_{field}_unknown_value_{value}");
        return fallback;
    }
}
