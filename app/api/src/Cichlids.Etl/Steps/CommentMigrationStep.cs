using System.Text.Json;
using Cichlids.Domain.Archive;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Identity;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy comments and star ratings (user_cichlids_comments) into the separate comment
/// and rating entities (ADR-0020), their up/down votes (user_cichlids_comments_rated) into
/// comment_vote, and recomputes the denormalized aggregates the two feed (post.rating_average,
/// post.rating_count and comment.score) from those base tables at the end of the run. A member
/// keeps one rating per post or tank, the newest legacy one, and a rating of the member's own post
/// or tank stays out while its comment text migrates. A legacy row whose target post or tank never
/// migrated, or whose type is neither 1 (picture) nor 2 (tank), is preserved in
/// archive.legacy_comment.
/// </summary>
public sealed class CommentMigrationStep : IEtlStep
{
    private const int BatchSize = 5000;
    private const int ProgressInterval = 100000;

    private static readonly ValueConverter<ProfileKind, string> ProfileKindConverter = new SnakeCaseEnumConverter<ProfileKind>();
    private static readonly ValueConverter<LegacyTargetType, string> TargetTypeConverter = new SnakeCaseEnumConverter<LegacyTargetType>();

    private static readonly IReadOnlyDictionary<string, string> VaultColumnCasts =
        new Dictionary<string, string> { ["payload"] = "jsonb" };

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public string Name => "comments";

    public int Order => 50;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var profileByLegacyId = await LoadProfilesAsync(context, cancellationToken);
        var postIdByLegacyId = await LoadLegacyIdMapAsync(
            context, "SELECT legacy_id, id FROM post WHERE legacy_id IS NOT NULL", cancellationToken);
        var tankIdByLegacyId = await LoadLegacyIdMapAsync(
            context, "SELECT legacy_id, id FROM tank WHERE legacy_id IS NOT NULL", cancellationToken);
        var allPictureUids = await LoadLegacyUidSetAsync(context, "user_cichlids_pictures", cancellationToken);
        var allTankUids = await LoadLegacyUidSetAsync(context, "user_cichlids_tanks", cancellationToken);
        var commentIdByLegacyId = new Dictionary<int, long>();
        var guestNames = await context.GetGuestNamesAsync(cancellationToken);

        var ratingSelection = await SelectRatingsAsync(context, profileByLegacyId, cancellationToken);
        if (ratingSelection.Collapsed > 0)
        {
            stats.AddSkip("rating_collapsed_older", ratingSelection.Collapsed);
        }

        if (ratingSelection.SelfDropped > 0)
        {
            stats.AddSkip("rating_self_dropped", ratingSelection.SelfDropped);
        }

        const string sql = """
            SELECT
                uid, type, item, rating, poster, note, fe_user, tstamp, crdate, deleted, hidden,
                delete_tstamp, delete_reason, delete_user, score
            FROM user_cichlids_comments
            ORDER BY uid
            """;

        var batch = new List<RawCommentRow>(BatchSize);
        var readCount = 0;

        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stats.AddRead();
                readCount++;
                if (readCount % ProgressInterval == 0)
                {
                    Console.WriteLine($"[comments] read {readCount} rows...");
                }

                if (reader.GetInt32("deleted") != 0)
                {
                    stats.AddSkip("comment_deleted");
                    continue;
                }

                batch.Add(new RawCommentRow(
                    reader.GetInt32("uid"),
                    reader.GetInt32("type"),
                    reader.GetNullableInt32("item") ?? 0,
                    reader.GetInt32("rating"),
                    reader.GetTrimmedOrNull("poster"),
                    reader.GetTrimmedOrNull("note"),
                    reader.GetInt32("fe_user"),
                    reader.GetInt64("tstamp"),
                    reader.GetInt64("crdate"),
                    reader.GetInt32("hidden") != 0,
                    reader.GetInt64("delete_tstamp"),
                    reader.GetTrimmedOrNull("delete_reason"),
                    reader.GetNullableInt32("delete_user"),
                    reader.GetInt32("score")));

                if (batch.Count >= BatchSize)
                {
                    await ProcessBatchAsync(
                        context, batch, profileByLegacyId, postIdByLegacyId, tankIdByLegacyId,
                        allPictureUids, allTankUids, commentIdByLegacyId, ratingSelection.Winners, guestNames, stats,
                        cancellationToken);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            await ProcessBatchAsync(
                context, batch, profileByLegacyId, postIdByLegacyId, tankIdByLegacyId,
                allPictureUids, allTankUids, commentIdByLegacyId, ratingSelection.Winners, guestNames, stats,
                cancellationToken);
        }

        await MigrateCommentVotesAsync(context, profileByLegacyId, commentIdByLegacyId, stats, cancellationToken);
        await RecomputeAggregatesAsync(context, stats, cancellationToken);
    }

    private static async Task ProcessBatchAsync(
        EtlContext context,
        List<RawCommentRow> batch,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        Dictionary<int, long> postIdByLegacyId,
        Dictionary<int, long> tankIdByLegacyId,
        HashSet<int> allPictureUids,
        HashSet<int> allTankUids,
        Dictionary<int, long> commentIdByLegacyId,
        IReadOnlySet<int> ratingWinners,
        GuestNames guestNames,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        var vaultRows = new List<IReadOnlyList<object?>>();
        var commentRows = new List<IReadOnlyList<object?>>();
        var ratingRows = new List<IReadOnlyList<object?>>();

        foreach (var row in batch)
        {
            var (postId, tankId, vaultReason, vaultTargetType) = ResolveTarget(
                row.Type, row.Item, postIdByLegacyId, tankIdByLegacyId, allPictureUids, allTankUids);

            var createdAt = row.Crdate > 0
                ? DateTimeOffset.FromUnixTimeSeconds(row.Crdate)
                : DateTimeOffset.FromUnixTimeSeconds(row.Tstamp);

            if (vaultReason is not null)
            {
                stats.AddSkip($"comment_vaulted_{vaultReason}");
                vaultRows.Add(BuildVaultRow(row, vaultTargetType, vaultReason, createdAt));
                continue;
            }

            var noteEmpty = row.Note is null;
            var hasRating = row.Rating > 0;
            if (noteEmpty && !hasRating)
            {
                stats.AddSkip("comment_content_empty");
                continue;
            }

            // The rating selection counts every rating it leaves out as collapsed or self-rated.
            var migratesRating = hasRating && ratingWinners.Contains(row.LegacyId);
            if (noteEmpty && !migratesRating)
            {
                continue;
            }

            var (authorProfileId, posterName) = await ResolveAuthorAsync(
                context, row.FeUser, row.Poster, profileByLegacyId, guestNames, stats, cancellationToken);

            var moderated = row.Hidden || row.DeleteTstamp > 0;

            if (!noteEmpty)
            {
                DateTimeOffset? deletedAt = null;
                string? deleteReason = null;
                long? deletedByProfileId = null;

                if (moderated)
                {
                    deletedAt = DateTimeOffset.FromUnixTimeSeconds(row.DeleteTstamp > 0 ? row.DeleteTstamp : row.Tstamp);
                    deleteReason = row.DeleteReason;
                    deletedByProfileId = ResolveExistingProfileId(row.DeleteUser, profileByLegacyId);
                }

                commentRows.Add(new object?[]
                {
                    row.LegacyId, postId, tankId, authorProfileId, posterName, row.Note, row.Score,
                    createdAt, deletedAt, deleteReason, deletedByProfileId,
                });
            }

            if (migratesRating)
            {
                var stars = (short)Math.Min(5, Math.Max(1, row.Rating));
                ratingRows.Add(new object?[] { row.LegacyId, postId, tankId, authorProfileId, stars, createdAt });

                if (!noteEmpty && moderated)
                {
                    // The comment text is moderated away, but the star rating is a separate entity
                    // and still migrates; kept as its own counter rather than a warning per row.
                    stats.AddSkip("comment_rating_from_hidden_comment");
                }
            }
        }

        if (vaultRows.Count > 0)
        {
            var vaultInserted = await PgBulkInsert.InsertOnConflictDoNothingAsync(
                context.Target,
                context.Transaction,
                "archive.legacy_comment",
                ["legacy_id"],
                [
                    "legacy_id", "legacy_target_type", "legacy_target_id", "author_legacy_user_id",
                    "poster_name", "body", "stars", "created_at_legacy", "payload", "vault_reason", "imported_at",
                ],
                vaultRows,
                cancellationToken,
                VaultColumnCasts);
            stats.AddInserted(vaultInserted);
        }

        if (commentRows.Count > 0)
        {
            var commentResults = await PgBatchUpsert.UpsertBatchAsync(
                context.Target,
                context.Transaction,
                "comment",
                "legacy_id",
                [
                    "legacy_id", "post_id", "tank_id", "author_profile_id", "poster_name", "body", "score",
                    "created_at", "deleted_at", "delete_reason", "deleted_by_profile_id",
                ],
                commentRows,
                cancellationToken);

            foreach (var (legacyId, result) in commentResults)
            {
                commentIdByLegacyId[legacyId] = result.Id;
                if (result.Inserted)
                {
                    stats.AddInserted();
                }
                else
                {
                    stats.AddUpdated();
                }
            }
        }

        if (ratingRows.Count > 0)
        {
            var ratingResults = await PgBatchUpsert.UpsertBatchAsync(
                context.Target,
                context.Transaction,
                "rating",
                "legacy_comment_id",
                ["legacy_comment_id", "post_id", "tank_id", "profile_id", "stars", "created_at"],
                ratingRows,
                cancellationToken);

            foreach (var (_, result) in ratingResults)
            {
                if (result.Inserted)
                {
                    stats.AddInserted();
                }
                else
                {
                    stats.AddUpdated();
                }
            }
        }
    }

    /// <summary>
    /// Reads every non-deleted legacy rating on a migrated post or tank and selects the ones that
    /// migrate with RatingSelection. A rating is a self-rating when the rater's profile is the
    /// author profile of the rated post or the owner profile of the rated tank. Anonymous ratings
    /// have no rater profile.
    /// </summary>
    private static async Task<RatingSelectionResult> SelectRatingsAsync(
        EtlContext context,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        CancellationToken cancellationToken)
    {
        var postAuthorByLegacyId = await LoadLegacyIdMapAsync(
            context, "SELECT legacy_id, author_profile_id FROM post WHERE legacy_id IS NOT NULL", cancellationToken);
        var tankOwnerByLegacyId = await LoadLegacyIdMapAsync(
            context, "SELECT legacy_id, profile_id FROM tank WHERE legacy_id IS NOT NULL", cancellationToken);

        var candidates = new List<RatingCandidate>();
        await using (var command = new MySqlCommand(
            "SELECT uid, type, item, fe_user, crdate, tstamp FROM user_cichlids_comments WHERE deleted = 0 AND rating > 0",
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = reader.GetInt32("type");
                var item = reader.GetNullableInt32("item") ?? 0;
                var owners = type switch
                {
                    1 => postAuthorByLegacyId,
                    2 => tankOwnerByLegacyId,
                    _ => null,
                };

                if (owners is null || !owners.TryGetValue(item, out var ownerProfileId))
                {
                    continue;
                }

                var feUser = reader.GetInt32("fe_user");
                var isSelf = feUser != 0
                    && profileByLegacyId.TryGetValue(feUser, out var rater)
                    && rater.Id == ownerProfileId;
                var crdate = reader.GetInt64("crdate");

                candidates.Add(new RatingCandidate(
                    reader.GetInt32("uid"), feUser, type, item, crdate > 0 ? crdate : reader.GetInt64("tstamp"), isSelf));
            }
        }

        return RatingSelection.Select(candidates);
    }

    /// <summary>
    /// Resolves a comment row's target post or tank. A resolved target returns exactly one of
    /// PostId/TankId with a null VaultReason; an unresolvable target (the
    /// legacy picture/tank was deleted or never migrated, the item id does not exist at all, or
    /// the legacy type is neither 1 nor 2) returns a reason instead, for the caller to vault the
    /// row rather than migrate it.
    /// </summary>
    private static (long? PostId, long? TankId, string? VaultReason, LegacyTargetType VaultTargetType) ResolveTarget(
        int type,
        int item,
        Dictionary<int, long> postIdByLegacyId,
        Dictionary<int, long> tankIdByLegacyId,
        HashSet<int> allPictureUids,
        HashSet<int> allTankUids)
    {
        if (type == 1)
        {
            if (postIdByLegacyId.TryGetValue(item, out var postId))
            {
                return (postId, null, null, default);
            }

            var reason = allPictureUids.Contains(item) ? "target_deleted" : "target_missing";
            return (null, null, reason, LegacyTargetType.Picture);
        }

        if (type == 2)
        {
            if (tankIdByLegacyId.TryGetValue(item, out var tankId))
            {
                return (null, tankId, null, default);
            }

            var reason = allTankUids.Contains(item) ? "target_deleted" : "target_missing";
            return (null, null, reason, LegacyTargetType.Tank);
        }

        // No legacy type other than 1 (picture) and 2 (tank) is ever recorded, but the vault path
        // still needs a target type to satisfy the archive table's check constraint; picture is
        // the default the item id is most likely to belong to.
        return (null, null, "unknown_type", LegacyTargetType.Picture);
    }

    private static object?[] BuildVaultRow(
        RawCommentRow row, LegacyTargetType targetType, string vaultReason, DateTimeOffset createdAt)
    {
        var payload = JsonSerializer.Serialize(row, PayloadOptions);
        short? stars = row.Rating > 0 ? (short)row.Rating : null;

        return
        [
            row.LegacyId,
            TargetTypeConverter.ConvertToProvider(targetType),
            row.Item,
            row.FeUser == 0 ? null : row.FeUser,
            row.Poster,
            row.Note,
            stars,
            createdAt,
            payload,
            vaultReason,
            DateTimeOffset.UtcNow,
        ];
    }

    /// <summary>
    /// Resolves a comment or rating's author: an anonymous legacy row (fe_user 0) has no profile
    /// and only a public poster name (its display name, or its generated guest name when the
    /// display name contains an email address), a legacy user with no migrated profile gets a
    /// placeholder (shared with every other step through PlaceholderProfiles), and everyone else
    /// resolves to their real profile with no display name of their own.
    /// </summary>
    private static async Task<(long? ProfileId, string? PosterName)> ResolveAuthorAsync(
        EtlContext context,
        int feUser,
        string? posterName,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        GuestNames guestNames,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (feUser == 0)
        {
            return (null, guestNames.Resolve(posterName));
        }

        if (profileByLegacyId.TryGetValue(feUser, out var profile))
        {
            return (profile.Id, null);
        }

        var placeholderId = await PlaceholderProfiles.EnsurePlaceholderProfileAsync(context, feUser, cancellationToken);
        profileByLegacyId[feUser] = (placeholderId, ProfileKind.Archived);
        stats.AddWarning($"comment author legacy user {feUser} has no migrated profile; created a placeholder profile.");
        return (placeholderId, null);
    }

    /// <summary>
    /// Resolves the acting moderator for a comment's deletion trail against whatever profile
    /// already exists for that legacy user id, without ever creating a placeholder: a moderator
    /// reference this loose (an admin action id, sometimes literally 0) is not itself proof the
    /// legacy user ever had migratable content of their own.
    /// </summary>
    private static long? ResolveExistingProfileId(
        int? legacyUserId, Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId)
    {
        if (legacyUserId is null or 0)
        {
            return null;
        }

        return profileByLegacyId.TryGetValue(legacyUserId.Value, out var profile) ? profile.Id : null;
    }

    /// <summary>
    /// Migrates up/down votes on comments (user_cichlids_comments_rated). The votes table holds a
    /// few tens of thousands of rows, so it is loaded and deduplicated in memory once the whole
    /// comment table has been processed and the comment id map is complete. The legacy timestamps
    /// are America/New_York wall-clock times and migrate converted to UTC.
    /// </summary>
    private static async Task MigrateCommentVotesAsync(
        EtlContext context,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        Dictionary<int, long> commentIdByLegacyId,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT comment_uid, fe_user, rated, tstamp
            FROM user_cichlids_comments_rated
            ORDER BY comment_uid, tstamp, fe_user
            """;

        var rows = new List<(int CommentUid, int FeUser, int Rated, DateTime Tstamp)>();
        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                // Not counted against stats.Read: that counter reports rows of the primary source
                // table (user_cichlids_comments) this step migrates, matching every other step's
                // convention of not counting rows read in a later, dependent pass.
                rows.Add((
                    reader.GetInt32("comment_uid"),
                    reader.GetInt32("fe_user"),
                    reader.GetInt32("rated"),
                    reader.GetDateTime("tstamp")));
            }
        }

        // Among rows sharing (comment_uid, fe_user), the earliest by tstamp is the one that
        // actually recorded the member's vote; every later duplicate is counted, not migrated.
        var winners = new List<(int CommentUid, int FeUser, int Rated, DateTime Tstamp)>();
        foreach (var group in rows.GroupBy(r => (r.CommentUid, r.FeUser)))
        {
            var ordered = group.OrderBy(r => r.Tstamp).ToList();
            winners.Add(ordered[0]);
            for (var i = 1; i < ordered.Count; i++)
            {
                stats.AddSkip("comment_vote_duplicate");
            }
        }

        var anonymous = new AnonymousArchiveAttribution(context, stats, "comment votes");
        var voteRows = new List<IReadOnlyList<object?>>();
        foreach (var row in winners)
        {
            if (!commentIdByLegacyId.TryGetValue(row.CommentUid, out var commentId))
            {
                stats.AddSkip("comment_vote_target_not_migrated");
                continue;
            }

            if (row.Rated is not (-1 or 1))
            {
                stats.AddSkip("comment_vote_invalid_value");
                continue;
            }

            var profileId = await ResolveVoterProfileIdAsync(context, row.FeUser, profileByLegacyId, anonymous, stats, cancellationToken);
            var createdAt = LegacyLocalTime.ToUtc(row.Tstamp);

            voteRows.Add(new object?[] { commentId, profileId, (short)row.Rated, createdAt });
        }

        if (voteRows.Count > 0)
        {
            var votesInserted = await PgBulkInsert.InsertOnConflictDoNothingAsync(
                context.Target,
                context.Transaction,
                "comment_vote",
                ["comment_id", "profile_id"],
                ["comment_id", "profile_id", "value", "created_at"],
                voteRows,
                cancellationToken);
            stats.AddInserted(votesInserted);
        }

        anonymous.Report();
    }

    private static async Task<long> ResolveVoterProfileIdAsync(
        EtlContext context,
        int feUser,
        Dictionary<int, (long Id, ProfileKind Kind)> profileByLegacyId,
        AnonymousArchiveAttribution anonymous,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (feUser == 0)
        {
            return await anonymous.ResolveAsync(cancellationToken);
        }

        if (profileByLegacyId.TryGetValue(feUser, out var profile))
        {
            return profile.Id;
        }

        var placeholderId = await PlaceholderProfiles.EnsurePlaceholderProfileAsync(context, feUser, cancellationToken);
        profileByLegacyId[feUser] = (placeholderId, ProfileKind.Archived);
        stats.AddWarning($"comment vote legacy user {feUser} has no migrated profile; created a placeholder profile.");
        return placeholderId;
    }

    /// <summary>
    /// Recomputes post.rating_average/rating_count and comment.score from the
    /// rating and comment_vote base tables (ADR-0020: derived aggregates are
    /// recomputed, never authored), unconditionally over the whole table rather than only the rows
    /// this run touched, so the aggregates stay correct however they drifted. Reports, purely as
    /// information, how many posts' newly computed average differs from the legacy denormalized
    /// value PictureMigrationStep seeded from user_cichlids_pictures.rating by
    /// more than 0.05.
    /// </summary>
    private static async Task RecomputeAggregatesAsync(EtlContext context, StepStatistics stats, CancellationToken cancellationToken)
    {
        var previousAverages = new Dictionary<long, double?>();
        await using (var select = new NpgsqlCommand("SELECT id, rating_average FROM post", context.Target, context.Transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                previousAverages[reader.GetInt64(0)] = reader.IsDBNull(1) ? null : reader.GetDouble(1);
            }
        }

        await using (var resetPost = new NpgsqlCommand(
            "UPDATE post SET rating_average = NULL, rating_count = 0", context.Target, context.Transaction))
        {
            await resetPost.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var applyPost = new NpgsqlCommand(
            """
            UPDATE post AS p SET rating_average = agg.avg_stars, rating_count = agg.cnt
            FROM (
                SELECT post_id, AVG(stars)::float8 AS avg_stars, COUNT(*) AS cnt
                FROM rating
                WHERE post_id IS NOT NULL
                GROUP BY post_id
            ) AS agg
            WHERE p.id = agg.post_id
            """,
            context.Target, context.Transaction))
        {
            await applyPost.ExecuteNonQueryAsync(cancellationToken);
        }

        var deviations = 0;
        await using (var select = new NpgsqlCommand("SELECT id, rating_average FROM post", context.Target, context.Transaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                double? newAverage = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                var previous = previousAverages.GetValueOrDefault(id);

                if (previous is null && newAverage is null)
                {
                    continue;
                }

                if (previous is null || newAverage is null || Math.Abs(previous.Value - newAverage.Value) > 0.05)
                {
                    deviations++;
                }
            }
        }

        stats.AddWarning(
            $"post rating_average recompute: {deviations} post(s) diverge from the legacy denormalized average by more than 0.05.");

        await using (var resetComment = new NpgsqlCommand("UPDATE comment SET score = 0", context.Target, context.Transaction))
        {
            await resetComment.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var applyComment = new NpgsqlCommand(
            """
            UPDATE comment AS c SET score = agg.total
            FROM (SELECT comment_id, SUM(value) AS total FROM comment_vote GROUP BY comment_id) AS agg
            WHERE c.id = agg.comment_id
            """,
            context.Target, context.Transaction))
        {
            await applyComment.ExecuteNonQueryAsync(cancellationToken);
        }
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

    /// <summary>
    /// Reads a target query returning a legacy id and a row or profile id per row into a map.
    /// </summary>
    private static async Task<Dictionary<int, long>> LoadLegacyIdMapAsync(
        EtlContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetInt32(0)] = reader.GetInt64(1);
        }

        return result;
    }

    private static async Task<HashSet<int>> LoadLegacyUidSetAsync(
        EtlContext context, string table, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand($"SELECT uid FROM {table}", context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    private sealed record RawCommentRow(
        int LegacyId,
        int Type,
        int Item,
        int Rating,
        string? Poster,
        string? Note,
        int FeUser,
        long Tstamp,
        long Crdate,
        bool Hidden,
        long DeleteTstamp,
        string? DeleteReason,
        int? DeleteUser,
        int Score);
}
