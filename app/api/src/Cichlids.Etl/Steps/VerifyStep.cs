using Cichlids.Etl.Runtime;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Checks the target database against the legacy source, entity by entity. Every check
/// recomputes its expected count from the legacy source (or, where a source-side formula would be
/// prohibitively expensive to reproduce, from a target-side consistency invariant) using its own
/// SQL and its own copy of each other step's filter rules, rather than reading the other steps'
/// run statistics: a bug shared between a step and its statistics counter would otherwise go
/// unnoticed. Prints its findings as a table and, with "--out", writes them as JSON.
/// </summary>
public sealed class VerifyStep : IEtlStep
{
    public string Name => "verify";

    public int Order => 80;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var report = new VerificationReport();

        await AddSpeciesRowsAsync(context, report, cancellationToken);
        await AddProfileAndIdentityRowsAsync(context, report, cancellationToken);
        await AddTankRowsAsync(context, report, cancellationToken);

        var migratedPostLegacyIds = await LoadLegacyIdSetAsync(context, "post", cancellationToken);
        var migratedTankLegacyIds = await LoadLegacyIdSetAsync(context, "tank", cancellationToken);
        var migratedSpeciesLegacyIds = await LoadLegacyIdSetAsync(context, "species", cancellationToken);

        var eligibleForumMessageIds = await AddDiscussionRowsAsync(context, report, cancellationToken);
        await AddMediaRowsAsync(context, report, eligibleForumMessageIds, cancellationToken);
        await AddPostRowsAsync(context, report, cancellationToken);
        await AddPostSpeciesRowsAsync(context, report, migratedPostLegacyIds, migratedSpeciesLegacyIds, cancellationToken);
        await AddSlugAliasRowsAsync(context, report, cancellationToken);
        var migratedCommentLegacyIds = await AddCommentRatingVaultRowsAsync(
            context, report, migratedPostLegacyIds, migratedTankLegacyIds, cancellationToken);
        await AddCommentVoteRowAsync(context, report, migratedCommentLegacyIds, cancellationToken);
        await AddAggregateConsistencyRowsAsync(context, report, cancellationToken);
        await AddCollectionRowsAsync(context, report, migratedPostLegacyIds, cancellationToken);
        await AddReferenceCheckRowsAsync(context, report, cancellationToken);

        VerificationReportPrinter.PrintTable(report);

        if (context.VerifyOutputPath is { } path)
        {
            await VerificationReportPrinter.WriteJsonAsync(report, path, cancellationToken);
        }

        context.VerificationReport = report;

        var stats = context.Statistics.ForStep(Name);
        stats.AddRead(report.Rows.Count);
        if (!report.Passed)
        {
            stats.AddWarning($"verification found {report.MismatchCount} mismatch(es).");
        }
    }

    // === species ===================================================================================

    private static async Task AddSpeciesRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var expected = await MySqlScalarAsync(
            context, "SELECT COUNT(*) FROM user_cichlids_species WHERE deleted = 0", cancellationToken);
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM species", cancellationToken);
        report.Add(VerificationRow.Compare("species", expected, actual));

        var slugCount = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM species WHERE slug IS NOT NULL", cancellationToken);
        report.Add(VerificationRow.Info("species_slug", slugCount));
    }

    // === profiles and identities ===================================================================

    /// <summary>
    /// Recomputes ProfileMigrationStep's eligibility formula (a non-deleted, non-disabled legacy
    /// user who owns at least one row in the pictures, tanks or comments table, deleted or not)
    /// and checks it against the target's migrated member profile count (kind member and a
    /// non-null legacy_id, which excludes profiles the API creates for a first login), then
    /// checks each identity provider's row count. Returns the eligible legacy user rows (with
    /// their openid/email columns) for the identity checks and for any future check that needs
    /// the same set.
    /// </summary>
    private static async Task AddProfileAndIdentityRowsAsync(
        EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var contentOwnerIds = new HashSet<int>();
        foreach (var table in (string[])["user_cichlids_pictures", "user_cichlids_tanks", "user_cichlids_comments"])
        {
            await using var command = new MySqlCommand($"SELECT DISTINCT fe_user FROM {table}", context.Legacy);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                contentOwnerIds.Add(reader.GetInt32(0));
            }
        }

        var eligibleUsers = new List<EligibleUser>();
        await using (var command = new MySqlCommand(
            """
            SELECT uid, tx_dixeasylogin_openid, email
            FROM fe_users
            WHERE deleted = 0 AND disable = 0
            """,
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var uid = reader.GetInt32(0);
                if (!contentOwnerIds.Contains(uid))
                {
                    continue;
                }

                eligibleUsers.Add(new EligibleUser(
                    uid,
                    reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : reader.GetString(2).Trim()));
            }
        }

        var actualMembers = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM profile WHERE kind = 'member' AND legacy_id IS NOT NULL", cancellationToken);
        report.Add(VerificationRow.Compare("profiles_member", eligibleUsers.Count, actualMembers));

        var actualArchived = await PgScalarAsync(context, "SELECT COUNT(*) FROM profile WHERE kind = 'archived'", cancellationToken);
        report.Add(VerificationRow.Info("profiles_archived", actualArchived));

        var actualMembersRegistered = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM profile WHERE kind = 'member' AND legacy_id IS NULL", cancellationToken);
        report.Add(VerificationRow.Info("profiles_member_registered", actualMembersRegistered));

        var actualSystem = await PgScalarAsync(context, "SELECT COUNT(*) FROM profile WHERE kind = 'system'", cancellationToken);
        report.Add(VerificationRow.Info("profiles_system", actualSystem));

        var eligibleIds = eligibleUsers.Select(u => u.Uid).ToHashSet();
        var expectedAuth0 = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new MySqlCommand(
            "SELECT sub, user_id FROM fe_users_auth0 WHERE user_id IS NOT NULL", context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (eligibleIds.Contains(reader.GetInt32(1)))
                {
                    expectedAuth0.Add(reader.GetString(0));
                }
            }
        }

        var expectedOpenId = eligibleUsers
            .Where(u => !string.IsNullOrEmpty(u.OpenId))
            .Select(u => u.OpenId!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedEmail = eligibleUsers
            .Where(u => !string.IsNullOrEmpty(u.Email))
            .Select(u => u.Email!.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var actualByProvider = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
            "SELECT provider, COUNT(*) FROM profile_identity GROUP BY provider", context.Target, context.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                actualByProvider[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        report.Add(VerificationRow.Compare(
            "identities_auth0", expectedAuth0.Count, actualByProvider.GetValueOrDefault("auth0")));
        report.Add(VerificationRow.Compare(
            "identities_legacy_openid", expectedOpenId.Count, actualByProvider.GetValueOrDefault("legacy-openid")));
        report.Add(VerificationRow.Compare(
            "identities_email", expectedEmail.Count, actualByProvider.GetValueOrDefault("email")));
    }

    private sealed record EligibleUser(int Uid, string? OpenId, string? Email);

    // === tanks ======================================================================================

    private static async Task AddTankRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var expected = await MySqlScalarAsync(
            context, "SELECT COUNT(*) FROM user_cichlids_tanks WHERE deleted = 0", cancellationToken);
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM tank", cancellationToken);
        report.Add(VerificationRow.Compare("tanks", expected, actual));

        var inhabitants = await PgScalarAsync(context, "SELECT COUNT(*) FROM inhabitant", cancellationToken);
        report.Add(VerificationRow.Info("tank_inhabitants", inhabitants));
    }

    // === discussion (forum threads and posts) ======================================================

    /// <summary>
    /// Recomputes ForumMigrationStep's thread and post eligibility (a visible, forum-mapped, not
    /// moved message whose own message id equals its thread column is a root; a post additionally
    /// needs its thread column to point at such a root) directly from the Phorum messages table,
    /// checks it against the target, and returns the eligible post message ids for the
    /// forum-attachment media check.
    /// </summary>
    private static async Task<HashSet<int>> AddDiscussionRowsAsync(
        EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        // status = 2 is Phorum's visible status; forum_id 1/2/3 are the three forums
        // ForumMigrationStep's CategoryByForumId maps into a discussion category, the same set
        // this check mirrors independently.
        var messages = new List<(int MessageId, int Thread, int ParentId, bool Moved)>();
        await using (var command = new MySqlCommand(
            """
            SELECT message_id, thread, parent_id, moved
            FROM cichlids_phorum5.phorum_messages
            WHERE status = 2 AND forum_id IN (1, 2, 3)
            """,
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetBoolean(3)));
            }
        }

        var rootIds = messages
            .Where(m => !m.Moved && m.ParentId == 0 && m.MessageId == m.Thread)
            .Select(m => m.MessageId)
            .ToHashSet();
        var eligiblePostIds = messages.Where(m => !m.Moved && rootIds.Contains(m.Thread)).Select(m => m.MessageId).ToHashSet();

        var actualThreads = await PgScalarAsync(context, "SELECT COUNT(*) FROM discussion_thread", cancellationToken);
        report.Add(VerificationRow.Compare("discussion_thread", rootIds.Count, actualThreads));

        var actualPosts = await PgScalarAsync(context, "SELECT COUNT(*) FROM discussion_post", cancellationToken);
        report.Add(VerificationRow.Compare("discussion_post", eligiblePostIds.Count, actualPosts));

        var postCountMismatches = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM discussion_thread t
            LEFT JOIN (SELECT thread_id, COUNT(*) AS c FROM discussion_post GROUP BY thread_id) p
                ON p.thread_id = t.id
            WHERE COALESCE(p.c, 0) <> t.post_count
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("discussion_thread_post_count_consistency", 0, postCountMismatches));

        return eligiblePostIds;
    }

    // === media (picture originals and forum attachments) ==========================================

    private static async Task AddMediaRowsAsync(
        EtlContext context, VerificationReport report, HashSet<int> eligibleForumMessageIds, CancellationToken cancellationToken)
    {
        var expectedOriginals = await MySqlScalarAsync(
            context,
            "SELECT COUNT(*) FROM user_cichlids_pictures WHERE NOT (deleted = 1 OR delete_tstamp > 0)",
            cancellationToken);
        var actualOriginals = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM media_item WHERE storage_key LIKE 'originals/%'", cancellationToken);
        report.Add(VerificationRow.Compare("media_originals", expectedOriginals, actualOriginals));

        var expectedAttachments = 0L;
        await using (var command = new MySqlCommand(
            "SELECT message_id FROM cichlids_phorum5.phorum_files WHERE link = 'message' AND message_id > 0",
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (eligibleForumMessageIds.Contains(reader.GetInt32(0)))
                {
                    expectedAttachments++;
                }
            }
        }

        var actualAttachments = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM media_item WHERE storage_key LIKE 'forum_attachments/%'", cancellationToken);
        report.Add(VerificationRow.Compare("media_forum_attachments", expectedAttachments, actualAttachments));
    }

    // === post (blog posts derived from pictures) ===================================================

    private static async Task AddPostRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        // pid 21/29/109/131 are the picture placements PictureMigrationStep's PostTopicByPid
        // turns into a post, the same set this check mirrors independently.
        var expected = await MySqlScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM user_cichlids_pictures
            WHERE NOT (deleted = 1 OR delete_tstamp > 0) AND pid IN (21, 29, 109, 131)
            """,
            cancellationToken);
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM post", cancellationToken);
        report.Add(VerificationRow.Compare("post", expected, actual));

        await using var command = new NpgsqlCommand(
            "SELECT state, COUNT(*) FROM post GROUP BY state", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var byState = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            byState[reader.GetString(0)] = reader.GetInt64(1);
        }

        report.Add(VerificationRow.Info("post_state_published", byState.GetValueOrDefault("published")));
        report.Add(VerificationRow.Info("post_state_draft", byState.GetValueOrDefault("draft")));
        report.Add(VerificationRow.Info("post_state_archived", byState.GetValueOrDefault("archived")));
    }

    // === post_species (picture-species links) =======================================================

    /// <summary>
    /// Recomputes SpeciesLinksStep's eligibility formula (a link with a nonzero species reference,
    /// whose picture resolved to a migrated post and whose species resolved to a migrated species)
    /// as the count of distinct (post, species) pairs directly from the legacy source, and checks
    /// it against post_species.
    /// </summary>
    private static async Task AddPostSpeciesRowsAsync(
        EtlContext context,
        VerificationReport report,
        HashSet<int> migratedPostLegacyIds,
        HashSet<int> migratedSpeciesLegacyIds,
        CancellationToken cancellationToken)
    {
        var distinctPairs = new HashSet<(int PostLegacyId, int SpeciesLegacyId)>();
        await using (var command = new MySqlCommand(
            "SELECT uid_local, uid_foreign FROM user_cichlids_species_pictures_mm WHERE uid_foreign <> 0",
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var postLegacyId = reader.GetInt32(0);
                var speciesLegacyId = reader.GetInt32(1);
                if (migratedPostLegacyIds.Contains(postLegacyId) && migratedSpeciesLegacyIds.Contains(speciesLegacyId))
                {
                    distinctPairs.Add((postLegacyId, speciesLegacyId));
                }
            }
        }

        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM post_species", cancellationToken);
        report.Add(VerificationRow.Compare("post_species", distinctPairs.Count, actual));
    }

    // === slug_alias (target-side consistency only) =================================================

    private static async Task AddSlugAliasRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var canonicalViolations = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM (
                SELECT post_id FROM slug_alias
                GROUP BY post_id
                HAVING SUM(CASE WHEN is_canonical THEN 1 ELSE 0 END) <> 1
            ) x
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("slug_alias_one_canonical_per_post", 0, canonicalViolations));

        var total = await PgScalarAsync(context, "SELECT COUNT(*) FROM slug_alias", cancellationToken);
        report.Add(VerificationRow.Info("slug_alias_total", total));

        var orphans = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM slug_alias sa LEFT JOIN post p ON p.id = sa.post_id WHERE p.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("slug_alias_orphans", 0, orphans));

        var uniqueViolations = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM (SELECT value FROM slug_alias GROUP BY value HAVING COUNT(*) > 1) x",
            cancellationToken);
        report.Add(VerificationRow.Compare("slug_alias_unique_value", 0, uniqueViolations));

        var postsWithoutAlias = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM post p LEFT JOIN slug_alias sa ON sa.post_id = p.id WHERE sa.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("post_without_slug_alias", 0, postsWithoutAlias));
    }

    // === comment, rating and archive.legacy_comment (vault) =========================================

    /// <summary>
    /// Recomputes CommentMigrationStep's per-row classification (unresolved target vaults the row;
    /// a resolved target with no text and no rating is dropped; otherwise a non-empty note becomes
    /// a comment and a positive rating becomes a rating, independently of each other) by streaming
    /// the whole legacy comments table, and checks the resulting counts against comment, rating and
    /// archive.legacy_comment. Returns the legacy ids of comments that migrated, for the vote check.
    /// </summary>
    private static async Task<HashSet<int>> AddCommentRatingVaultRowsAsync(
        EtlContext context,
        VerificationReport report,
        HashSet<int> migratedPostLegacyIds,
        HashSet<int> migratedTankLegacyIds,
        CancellationToken cancellationToken)
    {
        var vaultCount = 0L;
        var commentCount = 0L;
        var ratingCount = 0L;
        var migratedCommentLegacyIds = new HashSet<int>();

        await using (var command = new MySqlCommand(
            "SELECT uid, type, item, rating, note, deleted FROM user_cichlids_comments",
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(5) != 0)
                {
                    continue;
                }

                var legacyId = reader.GetInt32(0);
                var type = reader.GetInt32(1);
                var item = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var rating = reader.GetInt32(3);
                var note = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
                if (note?.Length == 0)
                {
                    note = null;
                }

                var validTarget = type switch
                {
                    1 => migratedPostLegacyIds.Contains(item),
                    2 => migratedTankLegacyIds.Contains(item),
                    _ => false,
                };

                if (!validTarget)
                {
                    // Every unresolved row lands in the vault regardless of why (an unmigrated
                    // target, a target id that never existed at all, or an unrecognized type): the
                    // distinction only matters for the step's own diagnostics, not for this count.
                    vaultCount++;
                    continue;
                }

                var hasRating = rating > 0;
                if (note is null && !hasRating)
                {
                    continue;
                }

                if (note is not null)
                {
                    commentCount++;
                    migratedCommentLegacyIds.Add(legacyId);
                }

                if (hasRating)
                {
                    ratingCount++;
                }
            }
        }

        var actualComments = await PgScalarAsync(context, "SELECT COUNT(*) FROM comment", cancellationToken);
        report.Add(VerificationRow.Compare("comment", commentCount, actualComments));

        var actualRatings = await PgScalarAsync(context, "SELECT COUNT(*) FROM rating", cancellationToken);
        report.Add(VerificationRow.Compare("rating", ratingCount, actualRatings));

        var actualVault = await PgScalarAsync(context, "SELECT COUNT(*) FROM archive.legacy_comment", cancellationToken);
        report.Add(VerificationRow.Compare("vault_legacy_comment", vaultCount, actualVault));

        return migratedCommentLegacyIds;
    }

    // === comment_vote ===============================================================================

    /// <summary>
    /// Recomputes MigrateCommentVotesAsync's dedupe (the earliest vote per comment/voter pair wins)
    /// and its two drop rules (an invalid value, or a comment that never migrated) from the legacy
    /// votes table, and checks the result against comment_vote.
    /// </summary>
    private static async Task AddCommentVoteRowAsync(
        EtlContext context, VerificationReport report, HashSet<int> migratedCommentLegacyIds, CancellationToken cancellationToken)
    {
        var seenPairs = new HashSet<(int CommentUid, int FeUser)>();
        var expectedVotes = 0L;

        await using (var command = new MySqlCommand(
            """
            SELECT comment_uid, fe_user, rated
            FROM user_cichlids_comments_rated
            ORDER BY comment_uid, tstamp, fe_user
            """,
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var commentUid = reader.GetInt32(0);
                var feUser = reader.GetInt32(1);
                if (!seenPairs.Add((commentUid, feUser)))
                {
                    continue;
                }

                var rated = reader.GetInt32(2);
                if (rated is not (-1 or 1))
                {
                    continue;
                }

                if (migratedCommentLegacyIds.Contains(commentUid))
                {
                    expectedVotes++;
                }
            }
        }

        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM comment_vote", cancellationToken);
        report.Add(VerificationRow.Compare("comment_vote", expectedVotes, actual));
    }

    // === aggregate consistency (target only) ========================================================

    private static async Task AddAggregateConsistencyRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var ratingCountMismatches = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM post p
            LEFT JOIN (SELECT post_id, COUNT(*) AS c FROM rating WHERE post_id IS NOT NULL GROUP BY post_id) r
                ON r.post_id = p.id
            WHERE COALESCE(r.c, 0) <> p.rating_count
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("post_rating_count_consistency", 0, ratingCountMismatches));

        var scoreMismatches = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM comment c
            LEFT JOIN (SELECT comment_id, SUM(value) AS s FROM comment_vote GROUP BY comment_id) v
                ON v.comment_id = c.id
            WHERE COALESCE(v.s, 0) <> c.score
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("comment_score_consistency", 0, scoreMismatches));
    }

    // === collection and collection_entry (galleries) ================================================

    private static async Task AddCollectionRowsAsync(
        EtlContext context, VerificationReport report, HashSet<int> migratedPostLegacyIds, CancellationToken cancellationToken)
    {
        var expectedCollections = await MySqlScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM user_cichlids_gallery g
            WHERE g.deleted = 0
              AND EXISTS (SELECT 1 FROM user_cichlids_gallery_pictures_mm mm WHERE mm.uid_gallery = g.uid)
            """,
            cancellationToken);
        var actualCollections = await PgScalarAsync(context, "SELECT COUNT(*) FROM collection", cancellationToken);
        report.Add(VerificationRow.Compare("collection", expectedCollections, actualCollections));

        var distinctEntries = new HashSet<(int Gallery, int Picture)>();
        await using (var command = new MySqlCommand(
            """
            SELECT mm.uid_gallery, mm.uid_picture
            FROM user_cichlids_gallery_pictures_mm mm
            JOIN user_cichlids_gallery g ON g.uid = mm.uid_gallery AND g.deleted = 0
            """,
            context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var picture = reader.GetInt32(1);
                if (migratedPostLegacyIds.Contains(picture))
                {
                    distinctEntries.Add((reader.GetInt32(0), picture));
                }
            }
        }

        var actualEntries = await PgScalarAsync(context, "SELECT COUNT(*) FROM collection_entry", cancellationToken);
        report.Add(VerificationRow.Compare("collection_entry", distinctEntries.Count, actualEntries));
    }

    // === reference and domain checks (target only) ==================================================

    private static async Task AddReferenceCheckRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var postsWithoutMedia = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM post p LEFT JOIN post_media pm ON pm.post_id = p.id WHERE pm.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("post_without_media", 0, postsWithoutMedia));

        var publishedWithoutCanonicalSlug = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM post p
            WHERE p.state = 'published'
              AND NOT EXISTS (SELECT 1 FROM slug_alias sa WHERE sa.post_id = p.id AND sa.is_canonical)
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("post_published_without_canonical_slug", 0, publishedWithoutCanonicalSlug));

        var archivedWithMemberAuthor = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM post p
            JOIN profile pr ON pr.id = p.author_profile_id
            WHERE p.state = 'archived' AND pr.kind = 'member'
            """,
            cancellationToken);
        report.Add(VerificationRow.Info("post_archived_author_member", archivedWithMemberAuthor));
    }

    // === shared helpers ==============================================================================

    private static async Task<HashSet<int>> LoadLegacyIdSetAsync(EtlContext context, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT legacy_id FROM {table} WHERE legacy_id IS NOT NULL", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    private static async Task<long> MySqlScalarAsync(EtlContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, context.Legacy);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    private static async Task<long> PgScalarAsync(EtlContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, context.Target, context.Transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }
}
