using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Persistence.Conversions;
using Cichlids.Infrastructure.Slugs;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy pictures (<c>user_cichlids_pictures</c>) into <c>media_item</c> and, for the
/// legacy "pid" values that represent a gallery listing rather than a technical/decoration
/// upload, into a matching <c>post</c> plus its <c>post_media</c> and <c>slug_alias</c> rows.
/// Also backfills <c>profile.profile_image_media_id</c>/<c>avatar_media_id</c> and
/// <c>tank.main_media_id</c>/<c>tank_media</c> once every picture has its own media item, since
/// those legacy image references only resolve after this step has run.
/// </summary>
public sealed class PictureMigrationStep : IEtlStep
{
    private const int BatchSize = 5000;
    private const int ProgressInterval = 20000;

    private const int TanksTechnicPid = 62;
    private const int TanksDecorationPid = 63;
    private const int TanksUploadPid = 137;
    private const int ProfileImagePid = 138;
    private const int AvatarPid = 139;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mpg", "avi", "wmv", "flv", "webm", "mkv", "m4v", "mp2", "m2v", "mpeg", "3gp", "3g2", "mov",
    };

    private static readonly Dictionary<int, PostTopic> PostTopicByPid = new()
    {
        [21] = PostTopic.Cichlids,
        [29] = PostTopic.Tanks,
        [109] = PostTopic.Offtopic,
        [131] = PostTopic.Contest,
    };

    private static readonly ValueConverter<ProfileKind, string> ProfileKindConverter = new SnakeCaseEnumConverter<ProfileKind>();
    private static readonly ValueConverter<MediaKind, string> MediaKindConverter = new SnakeCaseEnumConverter<MediaKind>();
    private static readonly ValueConverter<PostKind, string> PostKindConverter = new SnakeCaseEnumConverter<PostKind>();
    private static readonly ValueConverter<PostTopic, string> PostTopicConverter = new SnakeCaseEnumConverter<PostTopic>();
    private static readonly ValueConverter<PostState, string> PostStateConverter = new SnakeCaseEnumConverter<PostState>();
    private static readonly ValueConverter<TankMediaSection, string> SectionConverter = new SnakeCaseEnumConverter<TankMediaSection>();

    public string Name => "pictures";

    public int Order => 40;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var profileByLegacyId = await LoadProfilesAsync(context, cancellationToken);
        var storageKeyOwners = await LoadStorageKeyOwnersAsync(context, cancellationToken);
        var mediaIdByLegacyId = new Dictionary<int, long>();
        var postIdByLegacyId = new Dictionary<int, long>();

        const string sql = """
            SELECT
                uid, pid, tstamp, crdate, deleted, hidden, title, fe_user, image, description,
                rating, rating_count, views, delete_tstamp
            FROM user_cichlids_pictures
            ORDER BY uid
            """;

        var batch = new List<ActivePicture>(BatchSize);
        var readCount = 0;

        // Scoped so the reader (and the legacy connection it occupies) is fully disposed before
        // the backfill/tank-media/slug-alias passes below run their own legacy queries: a MySQL
        // connection can only serve one open reader at a time, and reaching end-of-results does
        // not by itself release it.
        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stats.AddRead();
                readCount++;
                if (readCount % ProgressInterval == 0)
                {
                    Console.WriteLine($"[pictures] read {readCount} rows...");
                }

                var deleted = reader.GetInt32("deleted") != 0 || reader.GetInt64("delete_tstamp") > 0;
                if (deleted)
                {
                    stats.AddSkip("picture_deleted");
                    continue;
                }

                var legacyId = reader.GetInt32("uid");
                var feUser = reader.GetInt32("fe_user");
                var tstamp = reader.GetInt64("tstamp");
                var crdate = reader.GetInt64("crdate");
                var createdAt = DateTimeOffset.FromUnixTimeSeconds(crdate > 0 ? crdate : tstamp);
                var owner = await ResolveOwnerAsync(context, feUser, profileByLegacyId, stats, cancellationToken);

                batch.Add(new ActivePicture(
                    legacyId,
                    reader.GetInt32("pid"),
                    reader.GetStringOrEmpty("image"),
                    reader.GetTrimmedOrNull("title"),
                    reader.GetTrimmedOrNull("description"),
                    reader.GetInt32("hidden") != 0,
                    reader.GetInt32("views"),
                    reader.GetInt32("rating_count"),
                    reader.GetFloat("rating"),
                    tstamp,
                    createdAt,
                    owner.ProfileId,
                    owner.Kind));

                if (batch.Count >= BatchSize)
                {
                    await ProcessBatchAsync(context, batch, storageKeyOwners, mediaIdByLegacyId, postIdByLegacyId, stats, cancellationToken);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            await ProcessBatchAsync(context, batch, storageKeyOwners, mediaIdByLegacyId, postIdByLegacyId, stats, cancellationToken);
        }

        await BackfillMediaLinkAsync(
            context, profileByLegacyId, mediaIdByLegacyId,
            "user_cichlids_profile_image", "profile_image_media_id",
            "profile_image_backfill_no_profile", "profile_image_backfill_no_media",
            stats, cancellationToken);

        await BackfillMediaLinkAsync(
            context, profileByLegacyId, mediaIdByLegacyId,
            "user_cichlids_avatar_image", "avatar_media_id",
            "avatar_backfill_no_profile", "avatar_backfill_no_media",
            stats, cancellationToken);

        await PopulateTankMediaAsync(context, mediaIdByLegacyId, stats, cancellationToken);
        await PopulateSlugAliasesAsync(context, postIdByLegacyId, stats, cancellationToken);
    }

    private static async Task ProcessBatchAsync(
        EtlContext context,
        List<ActivePicture> batch,
        Dictionary<string, int> storageKeyOwners,
        Dictionary<int, long> mediaIdByLegacyId,
        Dictionary<int, long> postIdByLegacyId,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        var mediaRows = new List<IReadOnlyList<object?>>(batch.Count);
        foreach (var picture in batch)
        {
            var kind = DetermineKind(picture.Image);
            var storageKey = ResolveStorageKey(picture.Image, picture.LegacyId, storageKeyOwners, stats);

            mediaRows.Add(new object?[]
            {
                picture.LegacyId,
                picture.OwnerProfileId,
                MediaKindConverter.ConvertToProvider(kind),
                storageKey,
                StorageKeyNormalizer.GetOriginalFilename(picture.Image),
                picture.CreatedAt,
            });
        }

        var mediaResults = await PgBatchUpsert.UpsertBatchAsync(
            context.Target,
            context.Transaction,
            "media_item",
            "legacy_id",
            ["legacy_id", "owner_profile_id", "kind", "storage_key", "original_filename", "created_at"],
            mediaRows,
            cancellationToken);

        foreach (var picture in batch)
        {
            var (mediaId, inserted) = mediaResults[picture.LegacyId];
            mediaIdByLegacyId[picture.LegacyId] = mediaId;

            if (inserted)
            {
                stats.AddInserted();
            }
            else
            {
                stats.AddUpdated();
            }
        }

        var postPictures = new List<ActivePicture>();
        foreach (var picture in batch)
        {
            if (PostTopicByPid.ContainsKey(picture.Pid))
            {
                postPictures.Add(picture);
            }
            else if (picture.Pid is TanksTechnicPid or TanksDecorationPid or TanksUploadPid or ProfileImagePid or AvatarPid)
            {
                // Tank technic/decoration/upload originals and profile image/avatar sources: a
                // media item is all they ever need, picked up by name elsewhere in this step.
            }
            else
            {
                stats.AddSkip($"picture_unmapped_pid_{picture.Pid}");
            }
        }

        if (postPictures.Count == 0)
        {
            return;
        }

        var postRows = new List<IReadOnlyList<object?>>(postPictures.Count);
        foreach (var picture in postPictures)
        {
            var topic = PostTopicByPid[picture.Pid];
            var isArchivedOwner = picture.OwnerKind is ProfileKind.Archived or ProfileKind.System;
            var state = isArchivedOwner
                ? PostState.Archived
                : picture.Hidden ? PostState.Draft : PostState.Published;
            var publishedAt = state == PostState.Published
                ? DateTimeOffset.FromUnixTimeSeconds(picture.Tstamp)
                : (DateTimeOffset?)null;
            var ratingAverage = picture.Rating > 0 ? (double?)picture.Rating : null;

            postRows.Add(new object?[]
            {
                picture.LegacyId,
                picture.OwnerProfileId,
                PostKindConverter.ConvertToProvider(PostKind.Single),
                PostTopicConverter.ConvertToProvider(topic),
                PostStateConverter.ConvertToProvider(state),
                publishedAt,
                picture.Title,
                picture.Description,
                (long)picture.Views,
                picture.RatingCount,
                ratingAverage,
                picture.CreatedAt,
            });
        }

        var postResults = await PgBatchUpsert.UpsertBatchAsync(
            context.Target,
            context.Transaction,
            "post",
            "legacy_id",
            [
                "legacy_id", "author_profile_id", "kind", "topic", "state", "published_at",
                "title", "description", "view_count", "rating_count", "rating_average", "created_at",
            ],
            postRows,
            cancellationToken);

        var postMediaRows = new List<(long PostId, long MediaItemId)>(postPictures.Count);
        foreach (var picture in postPictures)
        {
            var postId = postResults[picture.LegacyId].Id;
            postIdByLegacyId[picture.LegacyId] = postId;
            postMediaRows.Add((postId, mediaIdByLegacyId[picture.LegacyId]));
        }

        await InsertPostMediaAsync(context.Target, context.Transaction, postMediaRows, cancellationToken);
    }

    private static async Task<(long ProfileId, ProfileKind Kind)> ResolveOwnerAsync(
        EtlContext context,
        int feUser,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (feUser == 0)
        {
            stats.AddWarning("picture owner fe_user is 0 (anonymous); attributed to the community archive profile.");
            var communityArchiveId = await PlaceholderProfiles.EnsureCommunityArchiveProfileAsync(context, cancellationToken);
            return (communityArchiveId, ProfileKind.System);
        }

        if (profileByLegacyId.TryGetValue(feUser, out var profile))
        {
            return profile;
        }

        var placeholderId = await PlaceholderProfiles.EnsurePlaceholderProfileAsync(context, feUser, cancellationToken);
        var resolved = (placeholderId, ProfileKind.Archived);
        profileByLegacyId[feUser] = resolved;
        stats.AddWarning($"picture owner legacy user {feUser} has no migrated profile; created a placeholder profile.");
        return resolved;
    }

    private static MediaKind DetermineKind(string image)
    {
        var lastDot = image.LastIndexOf('.');
        if (lastDot < 0 || lastDot == image.Length - 1)
        {
            return MediaKind.Photo;
        }

        var extension = image[(lastDot + 1)..];
        return VideoExtensions.Contains(extension) ? MediaKind.Video : MediaKind.Photo;
    }

    /// <summary>
    /// Resolves the deterministic storage key for one picture, resolving a collision against a
    /// key a DIFFERENT legacy id already owns by appending this row's legacy id before the file
    /// extension. Stable across runs: the same legacy id always resolves the same way, because
    /// <paramref name="storageKeyOwners"/> is seeded from the currently persisted state and the
    /// same collisions recur in the same order against the same data.
    /// </summary>
    private static string ResolveStorageKey(
        string image, int legacyId, Dictionary<string, int> storageKeyOwners, StepStatistics stats)
    {
        var baseKey = StorageKeyNormalizer.ToStorageKey(image);

        if (storageKeyOwners.TryGetValue(baseKey, out var owningLegacyId))
        {
            if (owningLegacyId == legacyId)
            {
                return baseKey;
            }

            stats.AddSkip("media_storage_key_collision");
            var suffixedKey = InsertLegacyIdSuffix(baseKey, legacyId);
            storageKeyOwners[suffixedKey] = legacyId;
            return suffixedKey;
        }

        storageKeyOwners[baseKey] = legacyId;
        return baseKey;
    }

    private static string InsertLegacyIdSuffix(string storageKey, int legacyId)
    {
        var lastSlash = storageKey.LastIndexOf('/');
        var directory = lastSlash >= 0 ? storageKey[..(lastSlash + 1)] : string.Empty;
        var filename = lastSlash >= 0 ? storageKey[(lastSlash + 1)..] : storageKey;

        var lastDot = filename.LastIndexOf('.');
        return lastDot < 0
            ? $"{directory}{filename}-{legacyId}"
            : $"{directory}{filename[..lastDot]}-{legacyId}{filename[lastDot..]}";
    }

    private static async Task InsertPostMediaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<(long PostId, long MediaItemId)> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var valuesSql = new List<string>(rows.Count);
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };

        var paramIndex = 0;
        foreach (var (postId, mediaItemId) in rows)
        {
            var postParam = $"p{paramIndex++}";
            var mediaParam = $"p{paramIndex++}";
            command.Parameters.AddWithValue(postParam, postId);
            command.Parameters.AddWithValue(mediaParam, mediaItemId);
            valuesSql.Add($"(@{postParam}, @{mediaParam}, 0)");
        }

        command.CommandText = $"""
            INSERT INTO post_media (post_id, media_item_id, sort)
            VALUES {string.Join(", ", valuesSql)}
            ON CONFLICT (post_id, media_item_id) DO NOTHING
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Backfills one profile media reference column (<c>profile_image_media_id</c> or
    /// <c>avatar_media_id</c>) from the matching legacy <c>fe_users</c> column, for existing
    /// profiles and migrated media items only: this never creates a placeholder profile, since a
    /// legacy user with no migrated profile at all has nothing to attach an image to.
    /// </summary>
    private static async Task BackfillMediaLinkAsync(
        EtlContext context,
        IReadOnlyDictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        IReadOnlyDictionary<int, long> mediaIdByLegacyId,
        string legacyColumn,
        string targetColumn,
        string noProfileSkipReason,
        string noMediaSkipReason,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT uid, {legacyColumn}
            FROM fe_users
            WHERE {legacyColumn} IS NOT NULL AND {legacyColumn} > 0
            ORDER BY uid
            """;

        var rows = new List<(int Uid, int PictureLegacyId)>();
        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        foreach (var (uid, pictureLegacyId) in rows)
        {
            if (!profileByLegacyId.TryGetValue(uid, out var profile))
            {
                stats.AddSkip(noProfileSkipReason);
                continue;
            }

            if (!mediaIdByLegacyId.TryGetValue(pictureLegacyId, out var mediaId))
            {
                stats.AddSkip(noMediaSkipReason);
                continue;
            }

            await using var update = new NpgsqlCommand(
                $"UPDATE profile SET {targetColumn} = @mediaId WHERE id = @profileId", context.Target, context.Transaction);
            update.Parameters.AddWithValue("mediaId", mediaId);
            update.Parameters.AddWithValue("profileId", profile.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Attaches <c>tank.main_media_id</c> and replaces each tank's <c>tank_media</c> rows from
    /// the legacy image/tank_images/deco_images/tec_images columns, now that every migrated
    /// picture has a media item to point at. All writes run as set-based statements over the
    /// whole tank population (one main-image update pair, one delete, chunked inserts): a round
    /// trip per tank would dominate this phase's run time.
    /// </summary>
    private static async Task PopulateTankMediaAsync(
        EtlContext context, Dictionary<int, long> mediaIdByLegacyId, StepStatistics stats, CancellationToken cancellationToken)
    {
        var tankIdByLegacyId = await LoadLegacyIdMapAsync(context, "tank", cancellationToken);

        // These four legacy columns are BLOB (binary charset), not TEXT, even though they only
        // ever hold plain ASCII CSV text: MySqlConnector surfaces a BLOB column as byte[] rather
        // than string, so the CAST forces it to send them as text the normal string readers work
        // with.
        const string sql = """
            SELECT
                uid,
                CAST(image AS CHAR) AS image,
                CAST(tank_images AS CHAR) AS tank_images,
                CAST(deco_images AS CHAR) AS deco_images,
                CAST(tec_images AS CHAR) AS tec_images
            FROM user_cichlids_tanks
            WHERE deleted = 0
            ORDER BY uid
            """;

        var mainImageTankIds = new List<long>();
        var mainImageMediaIds = new List<long>();
        var noMainImageTankIds = new List<long>();
        var allTankIds = new List<long>();
        var tankMediaRows = new List<IReadOnlyList<object?>>();

        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var tankLegacyId = reader.GetInt32("uid");
                if (!tankIdByLegacyId.TryGetValue(tankLegacyId, out var tankId))
                {
                    continue;
                }

                allTankIds.Add(tankId);

                var imageText = reader.GetTrimmedOrNull("image");
                long? mainMediaId = null;
                if (imageText is not null && int.TryParse(imageText, out var imageLegacyId) && imageLegacyId != 0)
                {
                    if (mediaIdByLegacyId.TryGetValue(imageLegacyId, out var mediaId))
                    {
                        mainMediaId = mediaId;
                    }
                    else
                    {
                        stats.AddSkip("tank_main_media_missing");
                    }
                }

                if (mainMediaId is not null)
                {
                    mainImageTankIds.Add(tankId);
                    mainImageMediaIds.Add(mainMediaId.Value);
                }
                else
                {
                    noMainImageTankIds.Add(tankId);
                }

                var perTankRows = new List<IReadOnlyList<object?>>();
                AppendTankMediaSection(reader.GetStringOrEmpty("tank_images"), TankMediaSection.Showcase, mediaIdByLegacyId, perTankRows, stats);
                AppendTankMediaSection(reader.GetStringOrEmpty("deco_images"), TankMediaSection.Decoration, mediaIdByLegacyId, perTankRows, stats);
                AppendTankMediaSection(reader.GetStringOrEmpty("tec_images"), TankMediaSection.Technic, mediaIdByLegacyId, perTankRows, stats);

                foreach (var row in perTankRows)
                {
                    tankMediaRows.Add(new object?[] { tankId, row[0], row[1], row[2] });
                }
            }
        }

        if (mainImageTankIds.Count > 0)
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE tank AS t SET main_media_id = u.media_id
                FROM (SELECT unnest(@tankIds) AS tank_id, unnest(@mediaIds) AS media_id) AS u
                WHERE t.id = u.tank_id
                """,
                context.Target, context.Transaction);
            update.Parameters.AddWithValue("tankIds", mainImageTankIds.ToArray());
            update.Parameters.AddWithValue("mediaIds", mainImageMediaIds.ToArray());
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        // A tank whose legacy image reference is empty or unresolvable converges to no main
        // image, so a re-run against source data that dropped the reference clears the column.
        if (noMainImageTankIds.Count > 0)
        {
            await using var clear = new NpgsqlCommand(
                "UPDATE tank SET main_media_id = NULL WHERE id = ANY(@tankIds)", context.Target, context.Transaction);
            clear.Parameters.AddWithValue("tankIds", noMainImageTankIds.ToArray());
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        if (allTankIds.Count > 0)
        {
            await using var delete = new NpgsqlCommand(
                "DELETE FROM tank_media WHERE tank_id = ANY(@tankIds)", context.Target, context.Transaction);
            delete.Parameters.AddWithValue("tankIds", allTankIds.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await BulkInsertAsync(
            context, "tank_media", ["tank_id", "media_item_id", "section", "sort"], tankMediaRows, cancellationToken);
    }

    // The legacy CSV columns are dirty in the same way tanks.fish is: leading commas, empty
    // tokens and literal "0" placeholders for removed entries, which are silently skipped rather
    // than counted; only a nonzero token that fails to resolve to a media item is a real anomaly.
    // sort is the dense position among the entries actually kept, not the raw CSV index.
    private static void AppendTankMediaSection(
        string csv,
        TankMediaSection section,
        Dictionary<int, long> mediaIdByLegacyId,
        List<IReadOnlyList<object?>> rows,
        StepStatistics stats)
    {
        if (csv.Length == 0)
        {
            return;
        }

        var seenMediaIds = new HashSet<long>();
        var sort = 0;
        foreach (var rawToken in csv.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0 || !int.TryParse(token, out var legacyId) || legacyId == 0)
            {
                continue;
            }

            if (!mediaIdByLegacyId.TryGetValue(legacyId, out var mediaId))
            {
                stats.AddSkip("tank_media_reference_missing");
                continue;
            }

            if (!seenMediaIds.Add(mediaId))
            {
                continue;
            }

            rows.Add(new object?[] { mediaId, SectionConverter.ConvertToProvider(section), sort });
            sort++;
        }
    }

    /// <summary>
    /// Builds each migrated post's slug_alias rows from its surviving legacy realurl aliases
    /// (after the global value-text collision pass), or generates one deterministic short slug
    /// for a post that ends up with none. Writes run as one set-based delete over all migrated
    /// posts followed by chunked inserts: a delete-and-insert round trip per post (well over a
    /// hundred thousand posts) would dominate the whole step's run time.
    /// </summary>
    private static async Task PopulateSlugAliasesAsync(
        EtlContext context, Dictionary<int, long> postIdByLegacyId, StepStatistics stats, CancellationToken cancellationToken)
    {
        var aliasesByPictureLegacyId = await LoadPictureAliasesAsync(context, stats, cancellationToken);

        // Every slug value's owning post, seeded from the persisted table so a re-run recognizes
        // a post's own previously generated slug as its own rather than as a foreign collision
        // (which would mint a fresh slug on every run), then updated with this run's legacy alias
        // values before any slug is generated so a generated slug can never land on a value a
        // legacy alias in this same run is about to claim.
        var slugValueOwners = await LoadSlugValueOwnersAsync(context, cancellationToken);

        var aliasRows = new List<IReadOnlyList<object?>>();
        var postsNeedingGeneratedSlug = new List<(int PictureLegacyId, long PostId)>();
        foreach (var (pictureLegacyId, postId) in postIdByLegacyId)
        {
            var surviving = aliasesByPictureLegacyId.TryGetValue(pictureLegacyId, out var list)
                ? list
                : [];

            if (surviving.Count == 0)
            {
                postsNeedingGeneratedSlug.Add((pictureLegacyId, postId));
            }
            else
            {
                var canonicalUid = ChooseCanonicalUid(surviving);
                foreach (var alias in surviving)
                {
                    aliasRows.Add(new object?[] { postId, alias.ValueAlias, alias.Uid == canonicalUid, DateTimeOffset.UtcNow });
                    slugValueOwners[alias.ValueAlias] = postId;
                }
            }
        }

        foreach (var (pictureLegacyId, postId) in postsNeedingGeneratedSlug)
        {
            var slug = GenerateShortSlug(context.SlugGenerator, pictureLegacyId, postId, slugValueOwners);
            aliasRows.Add(new object?[] { postId, slug, true, DateTimeOffset.UtcNow });
        }

        if (postIdByLegacyId.Count > 0)
        {
            await using var delete = new NpgsqlCommand(
                "DELETE FROM slug_alias WHERE post_id = ANY(@postIds)", context.Target, context.Transaction);
            delete.Parameters.AddWithValue("postIds", postIdByLegacyId.Values.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await BulkInsertAsync(
            context, "slug_alias", ["post_id", "value", "is_canonical", "created_at"], aliasRows, cancellationToken);
    }

    // Chunked so one statement never exceeds the PostgreSQL protocol limit of 65535 bind
    // parameters; 10000 rows of up to 4 columns stays well below it.
    private static async Task BulkInsertAsync(
        EtlContext context,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 10000;

        for (var offset = 0; offset < rows.Count; offset += chunkSize)
        {
            var chunk = rows.Skip(offset).Take(chunkSize).ToList();
            var valuesSql = new List<string>(chunk.Count);
            await using var insert = new NpgsqlCommand { Connection = context.Target, Transaction = context.Transaction };

            var paramIndex = 0;
            foreach (var row in chunk)
            {
                var placeholders = new List<string>(columns.Count);
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
                $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES {string.Join(", ", valuesSql)}";
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // A 5-character alphanumeric alias reads as an auto-generated hashid rather than a
    // human-chosen slug, but only when the post has another surviving alias to prefer instead: a
    // lone 5-character alias with no sibling has nothing to lose to and stays the canonical pick.
    private static int ChooseCanonicalUid(List<(int Uid, string ValueAlias)> surviving)
    {
        bool LooksLikeHashid((int Uid, string ValueAlias) alias) =>
            surviving.Count > 1 && alias.ValueAlias.Length == 5 && alias.ValueAlias.All(IsAsciiAlphanumeric);

        var nonHashidCandidates = surviving.Where(a => !LooksLikeHashid(a)).ToList();
        var pool = nonHashidCandidates.Count > 0 ? nonHashidCandidates : surviving;
        return pool.OrderBy(a => a.Uid).First().Uid;
    }

    private static bool IsAsciiAlphanumeric(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    // Deterministic across runs: the same legacy id, hashed with the same fixed input under the
    // same secret, always produces the same candidate slug. A value this post already owns (its
    // own slug from an earlier run) counts as available rather than as a collision, so the
    // resolved slug is stable from run to run; a genuine collision against a different post
    // retries through SlugGenerator's own counter-suffix mechanism.
    private static string GenerateShortSlug(
        SlugGenerator slugGenerator, int legacyId, long postId, Dictionary<string, long> slugValueOwners)
    {
        var slug = slugGenerator.GenerateUnique(
            $"post:{legacyId}",
            candidate => slugValueOwners.TryGetValue(candidate, out var owner) && owner != postId);
        slugValueOwners[slug] = postId;
        return slug;
    }

    private static async Task<Dictionary<int, List<(int Uid, string ValueAlias)>>> LoadPictureAliasesAsync(
        EtlContext context, StepStatistics stats, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT uid, value_alias, value_id
            FROM tx_realurl_uniqalias
            WHERE tablename = 'user_cichlids_pictures'
            ORDER BY value_id, uid
            """;

        var allRows = new List<(int Uid, string ValueAlias, int ValueId)>();
        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                allRows.Add((reader.GetInt32("uid"), reader.GetString("value_alias"), reader.GetInt32("value_id")));
            }
        }

        // Across the whole dataset, the row with the smallest uid keeps its literal value_alias
        // text; every other row sharing that text (on the same picture or a different one) is
        // dropped rather than renamed, so it never becomes a slug_alias row at all.
        var winnerUidByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in allRows)
        {
            if (!winnerUidByValue.TryGetValue(row.ValueAlias, out var currentWinner) || row.Uid < currentWinner)
            {
                winnerUidByValue[row.ValueAlias] = row.Uid;
            }
        }

        var result = new Dictionary<int, List<(int, string)>>();
        foreach (var row in allRows)
        {
            if (winnerUidByValue[row.ValueAlias] != row.Uid)
            {
                stats.AddSkip("picture_alias_value_collision");
                continue;
            }

            if (!result.TryGetValue(row.ValueId, out var list))
            {
                list = [];
                result[row.ValueId] = list;
            }

            list.Add((row.Uid, row.ValueAlias));
        }

        return result;
    }

    private static async Task<Dictionary<string, long>> LoadSlugValueOwnersAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT value, post_id FROM slug_alias", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }

        return result;
    }

    private static async Task<Dictionary<int, (long Id, ProfileKind Kind)>> LoadProfilesAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT legacy_id, id, kind FROM profile WHERE legacy_id IS NOT NULL", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, (long, ProfileKind)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = (ProfileKind)ProfileKindConverter.ConvertFromProvider(reader.GetString(2))!;
            result[reader.GetInt32(0)] = (reader.GetInt64(1), kind);
        }

        return result;
    }

    private static async Task<Dictionary<string, int>> LoadStorageKeyOwnersAsync(EtlContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT storage_key, legacy_id FROM media_item WHERE legacy_id IS NOT NULL", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<string, int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
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

    private sealed record ActivePicture(
        int LegacyId,
        int Pid,
        string Image,
        string? Title,
        string? Description,
        bool Hidden,
        int Views,
        int RatingCount,
        float Rating,
        long Tstamp,
        DateTimeOffset CreatedAt,
        long OwnerProfileId,
        ProfileKind OwnerKind);
}
