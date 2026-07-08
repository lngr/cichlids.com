using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy aquarium setups (<c>user_cichlids_tanks</c>) into <c>tank</c> and their
/// stocking lists into <c>inhabitant</c>. Tank media (<c>tank_media</c>, <c>tank.main_media_id</c>)
/// is attached later by <see cref="PictureMigrationStep"/>, once the pictures the legacy image
/// columns reference have their own migrated media items to point at.
/// </summary>
public sealed class TankMigrationStep : IEtlStep
{
    private static readonly ValueConverter<TankCategory, string> CategoryConverter = new SnakeCaseEnumConverter<TankCategory>();
    private static readonly ValueConverter<Domain.Enums.DimensionUnit, string> DimensionUnitConverter = new SnakeCaseEnumConverter<Domain.Enums.DimensionUnit>();
    private static readonly ValueConverter<TankState, string> StateConverter = new SnakeCaseEnumConverter<TankState>();

    public string Name => "tanks";

    public int Order => 30;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var profileIdsByLegacyId = await LoadLegacyIdMapAsync(context, "profile", cancellationToken);
        var speciesIdsByLegacyId = await LoadLegacyIdMapAsync(context, "species", cancellationToken);

        // fish is a legacy BLOB column even though it only ever holds plain ASCII CSV text;
        // MySqlConnector surfaces a BLOB as byte[] rather than string, so the CAST forces it to
        // send it as text like every other column here (fish_count is already TEXT natively).
        const string sql = """
            SELECT
                uid, tstamp, crdate, hidden, category, fe_user,
                title, description, gravel, plants, more_deco, light, light_duration,
                filtration, more_tec, water_ph, water_kh, water_gh, water_no2, water_no3,
                water_po4, more_water, food, more,
                width, height, depth, unit,
                CAST(fish AS CHAR) AS fish, fish_count
            FROM user_cichlids_tanks
            WHERE deleted = 0
            ORDER BY uid
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stats.AddRead();

            var legacyId = reader.GetInt32("uid");
            var feUser = reader.GetInt32("fe_user");
            var ownerProfileId = await ResolveOwnerAsync(context, feUser, profileIdsByLegacyId, stats, cancellationToken);

            var hidden = reader.GetInt32("hidden") != 0;
            var state = hidden ? TankState.Draft : TankState.Published;
            var tstamp = reader.GetInt64("tstamp");
            var crdate = reader.GetInt64("crdate");
            var publishedAt = state == TankState.Published ? DateTimeOffset.FromUnixTimeSeconds(tstamp) : (DateTimeOffset?)null;
            var createdAt = DateTimeOffset.FromUnixTimeSeconds(crdate > 0 ? crdate : tstamp);

            var category = MapCategory(reader.GetInt32("category"), stats);

            var title = reader.GetTrimmedOrNull("title") ?? $"Tank {legacyId}";

            var values = new (string, object?)[]
            {
                ("legacy_id", legacyId),
                ("profile_id", ownerProfileId),
                ("title", title),
                ("description", reader.GetTrimmedOrNull("description")),
                ("category", category is null ? null : CategoryConverter.ConvertToProvider(category.Value)),
                ("width_value", NullIfZero(reader.GetInt32("width"))),
                ("height_value", NullIfZero(reader.GetInt32("height"))),
                ("depth_value", NullIfZero(reader.GetInt32("depth"))),
                ("dimension_unit", DimensionUnitConverter.ConvertToProvider(MapDimensionUnit(reader.GetTrimmedOrNull("unit")))),
                ("gravel", reader.GetTrimmedOrNull("gravel")),
                ("plants", reader.GetTrimmedOrNull("plants")),
                ("decoration", reader.GetTrimmedOrNull("more_deco")),
                ("light", reader.GetTrimmedOrNull("light")),
                ("light_duration", reader.GetTrimmedOrNull("light_duration")),
                ("filtration", reader.GetTrimmedOrNull("filtration")),
                ("technic", reader.GetTrimmedOrNull("more_tec")),
                ("water_ph", reader.GetTrimmedOrNull("water_ph")),
                ("water_kh", reader.GetTrimmedOrNull("water_kh")),
                ("water_gh", reader.GetTrimmedOrNull("water_gh")),
                ("water_no2", reader.GetTrimmedOrNull("water_no2")),
                ("water_no3", reader.GetTrimmedOrNull("water_no3")),
                ("water_po4", reader.GetTrimmedOrNull("water_po4")),
                ("water_notes", reader.GetTrimmedOrNull("more_water")),
                ("food", reader.GetTrimmedOrNull("food")),
                ("notes", reader.GetTrimmedOrNull("more")),
                ("state", StateConverter.ConvertToProvider(state)),
                ("created_at", createdAt),
                ("published_at", publishedAt),
            };

            var (tankId, inserted) = await PgUpsert.UpsertAsync(
                context.Target, context.Transaction, "tank", "legacy_id", values, cancellationToken);

            if (inserted)
            {
                stats.AddInserted();
            }
            else
            {
                stats.AddUpdated();
            }

            var inhabitants = ParseInhabitants(
                reader.GetStringOrEmpty("fish"), reader.GetStringOrEmpty("fish_count"), speciesIdsByLegacyId, stats);
            await PgChildRows.ReplaceAsync(
                context.Target,
                context.Transaction,
                "inhabitant",
                "tank_id",
                tankId,
                ["species_id", "count", "sort"],
                inhabitants,
                cancellationToken);
        }
    }

    private static async Task<long> ResolveOwnerAsync(
        EtlContext context,
        int feUser,
        Dictionary<int, long> profileIdsByLegacyId,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (feUser == 0)
        {
            stats.AddWarning("tank owner fe_user is 0 (anonymous); attributed to the community archive profile.");
            return await PlaceholderProfiles.EnsureCommunityArchiveProfileAsync(context, cancellationToken);
        }

        if (profileIdsByLegacyId.TryGetValue(feUser, out var profileId))
        {
            return profileId;
        }

        var placeholderId = await PlaceholderProfiles.EnsurePlaceholderProfileAsync(context, feUser, cancellationToken);
        profileIdsByLegacyId[feUser] = placeholderId;
        stats.AddWarning($"tank owner legacy user {feUser} has no migrated profile; created a placeholder profile.");
        return placeholderId;
    }

    private static async Task<Dictionary<int, long>> LoadLegacyIdMapAsync(
        EtlContext context, string table, CancellationToken cancellationToken)
    {
        await using var command = new Npgsql.NpgsqlCommand(
            $"SELECT legacy_id, id FROM {table} WHERE legacy_id IS NOT NULL", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetInt32(0)] = reader.GetInt64(1);
        }

        return result;
    }

    private static int? NullIfZero(int value) => value == 0 ? null : value;

    // The legacy unit column is free text: "inches" was the default seen on export, but any
    // recorded value (however it reads) means an editor overrode the default and picked the
    // metric field in the edit form, which historically stored centimeters. Only a genuinely
    // empty/NULL column keeps the untouched default of inches.
    private static Domain.Enums.DimensionUnit MapDimensionUnit(string? unit) =>
        unit is null ? Domain.Enums.DimensionUnit.Inch : Domain.Enums.DimensionUnit.Cm;

    private static TankCategory? MapCategory(int value, StepStatistics stats) => value switch
    {
        1 => TankCategory.Tanganyika,
        2 => TankCategory.Malawi,
        3 => TankCategory.American,
        6 => TankCategory.African,
        7 => TankCategory.Community,
        8 => TankCategory.CentralAmerican,
        9 => TankCategory.SouthAmerican,
        _ => Unmapped(stats, value),
    };

    private static TankCategory? Unmapped(StepStatistics stats, int value)
    {
        stats.AddSkip($"tank_category_unmapped_{value}");
        return null;
    }

    /// <summary>
    /// Pairs the comma-separated species uid list (<c>fish</c>) with the newline-separated count
    /// list (<c>fish_count</c>) by position. The uid list is dirty (leading commas, literal "0"
    /// placeholders from removed rows), so a position with no resolvable species is only kept
    /// when it still carries a count; otherwise it is dropped rather than producing an empty
    /// inhabitant row.
    /// </summary>
    private static List<IReadOnlyList<object?>> ParseInhabitants(
        string fishCsv,
        string fishCountText,
        IReadOnlyDictionary<int, long> speciesIdsByLegacyId,
        StepStatistics stats)
    {
        // Split() on an empty string yields one empty element rather than zero, which would turn
        // the overwhelming majority of tanks (no stocking list recorded at all) into one phantom
        // position each. Treat a blank column as no tokens instead.
        var speciesTokens = fishCsv.Length == 0 ? [] : fishCsv.Split(',');
        var countLines = fishCountText.Length == 0 ? [] : fishCountText.Split(['\r', '\n'], StringSplitOptions.None);

        var positions = Math.Max(speciesTokens.Length, countLines.Length);
        var result = new List<IReadOnlyList<object?>>();

        for (var i = 0; i < positions; i++)
        {
            var token = i < speciesTokens.Length ? speciesTokens[i].Trim() : string.Empty;
            var countText = i < countLines.Length ? countLines[i].Trim() : string.Empty;

            long? speciesId = null;
            if (token.Length > 0 && int.TryParse(token, out var speciesLegacyId) && speciesLegacyId != 0
                && speciesIdsByLegacyId.TryGetValue(speciesLegacyId, out var resolvedId))
            {
                speciesId = resolvedId;
            }

            if (speciesId is null)
            {
                stats.AddSkip("tank_inhabitant_species_missing");
            }

            var count = int.TryParse(countText, out var parsedCount) ? parsedCount : (int?)null;

            if (speciesId is null && count is null)
            {
                continue;
            }

            result.Add(new object?[] { speciesId, count, i });
        }

        return result;
    }
}
