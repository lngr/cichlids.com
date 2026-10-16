using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates the non-deleted legacy picture galleries (user_cichlids_gallery) into collection and
/// their ordered listings (user_cichlids_gallery_pictures_mm) into collection_entry. A gallery
/// that never had a picture assignment is skipped by rule and counted under the skip reason
/// gallery_without_pictures. A gallery whose assigned pictures all failed to migrate still
/// becomes an empty collection. Runs after PictureMigrationStep, whose story.legacy_id rows
/// resolve each gallery entry's picture reference to its migrated story.
/// </summary>
public sealed class GalleryMigrationStep : IEtlStep
{
    // 2003-01-01 UTC: the legacy creation timestamp (crdate) is zero for every gallery row, and
    // tstamp itself is occasionally zero too, so there is no real creation moment to convert for
    // those. This fixed marker is deliberately earlier than the site's actual launch, so it reads
    // as "unknown" rather than as a plausible date (same convention as ProfileMigrationStep).
    private static readonly DateTimeOffset UnknownCreatedAtMarker = new(2003, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string Name => "galleries";

    public int Order => 60;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var profileIdsByLegacyId = await LoadLegacyIdMapAsync(context, "profile", cancellationToken);
        var storyIdByLegacyId = await LoadLegacyIdMapAsync(context, "story", cancellationToken);
        var entriesByGalleryUid = await LoadGalleryEntriesAsync(context, cancellationToken);
        var anonymous = new AnonymousArchiveAttribution(context, stats, "galleries");

        // A gallery without any mm row never had a picture assigned, so it is an album that was
        // created and never filled. It is skipped and counted. A gallery whose mm rows exist but
        // whose pictures failed to migrate is handled below, once the source row is read.
        var galleriesWithoutPictures = await CountGalleriesWithoutPicturesAsync(context, cancellationToken);
        if (galleriesWithoutPictures > 0)
        {
            stats.AddSkip("gallery_without_pictures", galleriesWithoutPictures);
        }

        const string sql = """
            SELECT g.uid, g.tstamp, g.hidden, g.title, g.fe_user
            FROM user_cichlids_gallery g
            WHERE g.deleted = 0
              AND EXISTS (SELECT 1 FROM user_cichlids_gallery_pictures_mm mm WHERE mm.uid_gallery = g.uid)
            ORDER BY g.uid
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stats.AddRead();

            var legacyId = reader.GetInt32("uid");
            var feUser = reader.GetInt32("fe_user");
            var tstamp = reader.GetInt64("tstamp");
            var hidden = reader.GetInt32("hidden") != 0;
            var title = reader.GetTrimmedOrNull("title") ?? $"Gallery {legacyId}";
            var createdAt = tstamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(tstamp) : UnknownCreatedAtMarker;

            var ownerProfileId = await ResolveOwnerAsync(context, feUser, profileIdsByLegacyId, anonymous, stats, cancellationToken);

            var values = new (string, object?)[]
            {
                ("legacy_id", legacyId),
                ("profile_id", ownerProfileId),
                ("title", title),
                ("is_public", !hidden),
                ("created_at", createdAt),
            };

            var (collectionId, inserted) = await PgUpsert.UpsertAsync(
                context.Target, context.Transaction, "collection", "legacy_id", values, cancellationToken);

            if (inserted)
            {
                stats.AddInserted();
            }
            else
            {
                stats.AddUpdated();
            }

            var pictureUidsInOrder = entriesByGalleryUid.TryGetValue(legacyId, out var list) ? list : [];
            var entryRows = BuildEntryRows(pictureUidsInOrder, storyIdByLegacyId, stats);

            if (entryRows.Count == 0)
            {
                stats.AddSkip("gallery_empty_after_filter");
            }

            await PgChildRows.ReplaceAsync(
                context.Target,
                context.Transaction,
                "collection_entry",
                "collection_id",
                collectionId,
                ["story_id", "sort"],
                entryRows,
                cancellationToken);
        }

        anonymous.Report();
    }

    // Duplicate picture references within the same gallery collapse to their first sorted
    // occurrence (collection_entry has a unique (collection_id, story_id) index) and the sort
    // position is dense among the survivors, not the raw mm sorting value: dropped/unmigrated
    // entries must not leave gaps.
    private static List<IReadOnlyList<object?>> BuildEntryRows(
        List<int> pictureUidsInOrder, Dictionary<int, long> storyIdByLegacyId, StepStatistics stats)
    {
        var rows = new List<IReadOnlyList<object?>>();
        var seenStoryIds = new HashSet<long>();
        var sort = 0;

        foreach (var pictureUid in pictureUidsInOrder)
        {
            if (!storyIdByLegacyId.TryGetValue(pictureUid, out var storyId))
            {
                stats.AddSkip("gallery_entry_picture_not_migrated");
                continue;
            }

            if (!seenStoryIds.Add(storyId))
            {
                stats.AddSkip("gallery_entry_duplicate_picture");
                continue;
            }

            rows.Add(new object?[] { storyId, sort });
            sort++;
        }

        return rows;
    }

    private static async Task<int> CountGalleriesWithoutPicturesAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*) FROM user_cichlids_gallery g
            WHERE g.deleted = 0
              AND NOT EXISTS (SELECT 1 FROM user_cichlids_gallery_pictures_mm mm WHERE mm.uid_gallery = g.uid)
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> ResolveOwnerAsync(
        EtlContext context,
        int feUser,
        Dictionary<int, long> profileIdsByLegacyId,
        AnonymousArchiveAttribution anonymous,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (feUser == 0)
        {
            return await anonymous.ResolveAsync(cancellationToken);
        }

        if (profileIdsByLegacyId.TryGetValue(feUser, out var profileId))
        {
            return profileId;
        }

        var placeholderId = await PlaceholderProfiles.EnsurePlaceholderProfileAsync(context, feUser, cancellationToken);
        profileIdsByLegacyId[feUser] = placeholderId;
        stats.AddWarning($"gallery owner legacy user {feUser} has no migrated profile; created a placeholder profile.");
        return placeholderId;
    }

    private static async Task<Dictionary<int, List<int>>> LoadGalleryEntriesAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT uid_gallery, uid_picture
            FROM user_cichlids_gallery_pictures_mm
            ORDER BY uid_gallery, sorting, uid_picture
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, List<int>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var galleryUid = reader.GetInt32(0);
            var pictureUid = reader.GetInt32(1);

            if (!result.TryGetValue(galleryUid, out var list))
            {
                list = [];
                result[galleryUid] = list;
            }

            list.Add(pictureUid);
        }

        return result;
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
