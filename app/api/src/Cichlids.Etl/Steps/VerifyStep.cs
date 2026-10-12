using Cichlids.Etl.Identity;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Identity;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Checks the target database against the legacy source, entity by entity. Every expected count
/// comes from the legacy source, computed with this step's own SQL and its own copy of each
/// migration rule, or from a target-side invariant that holds for every row, so a defect in a
/// migration step shows up as a mismatch. Actual counts cover whole target tables, because the
/// verification runs right after the import into an empty target and every row there has to
/// follow from a legacy row. Every report row names the migration rule its expectation follows
/// from. Prints its findings as a table and, with "--out", writes them as JSON.
/// </summary>
public sealed class VerifyStep : IEtlStep
{
    public string Name => "verify";

    public int Order => 80;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var report = new VerificationReport();

        // pid 21/29/109/131 are the picture placements PictureMigrationStep's PostTopicByPid
        // turns into a post, the same set this check mirrors independently.
        var postOwnerByPictureUid = await LoadLegacyOwnerMapAsync(
            context,
            """
            SELECT uid, fe_user FROM user_cichlids_pictures
            WHERE NOT (deleted = 1 OR delete_tstamp > 0) AND pid IN (21, 29, 109, 131)
            """,
            cancellationToken);
        var tankOwnerByTankUid = await LoadLegacyOwnerMapAsync(
            context, "SELECT uid, fe_user FROM user_cichlids_tanks WHERE deleted = 0", cancellationToken);
        var speciesUids = await LoadLegacyUidSetAsync(
            context, "SELECT uid FROM user_cichlids_species WHERE deleted = 0", cancellationToken);

        await AddSpeciesRowsAsync(context, report, speciesUids.Count, cancellationToken);
        await AddProfileAndIdentityRowsAsync(context, report, cancellationToken);
        await AddTankRowsAsync(context, report, tankOwnerByTankUid.Count, cancellationToken);

        var eligibleForumMessageIds = await AddDiscussionRowsAsync(context, report, cancellationToken);
        await AddMediaRowsAsync(context, report, eligibleForumMessageIds, cancellationToken);
        await AddPostRowsAsync(context, report, postOwnerByPictureUid.Count, cancellationToken);
        await AddPostSpeciesRowsAsync(context, report, postOwnerByPictureUid, speciesUids, cancellationToken);
        await AddSlugAliasRowsAsync(context, report, cancellationToken);
        var migratedCommentLegacyIds = await AddCommentRatingVaultRowsAsync(
            context, report, postOwnerByPictureUid, tankOwnerByTankUid, cancellationToken);
        await AddCommentVoteRowAsync(context, report, migratedCommentLegacyIds, cancellationToken);
        await AddAggregateConsistencyRowsAsync(context, report, cancellationToken);
        await AddCollectionRowsAsync(context, report, postOwnerByPictureUid, cancellationToken);
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

    private static async Task AddSpeciesRowsAsync(
        EtlContext context, VerificationReport report, long expected, CancellationToken cancellationToken)
    {
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM species", cancellationToken);
        report.Add(VerificationRow.Compare("species", expected, actual, MigrationRules.Species));

        var slugCount = await PgScalarAsync(
            context, "SELECT COUNT(*) FROM species WHERE slug IS NOT NULL", cancellationToken);
        report.Add(VerificationRow.Info("species_slug", slugCount, MigrationRules.SpeciesSlugs));
    }

    // === profiles and identities ===================================================================

    /// <summary>
    /// Recomputes ProfileMigrationStep's eligibility formula (a non-deleted, non-disabled legacy
    /// user who owns at least one row in the pictures, tanks or comments table, deleted or not)
    /// and checks it against the target's member profile count, then checks each legacy identity
    /// provider's row count and reports the oidc identities the account import links. Also counts
    /// the profiles whose public handle or display name holds an email address, and the profiles
    /// whose external avatar URL is a Gravatar URL, both of which have to be zero for every
    /// profile.
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
            context, "SELECT COUNT(*) FROM profile WHERE kind = 'member'", cancellationToken);
        report.Add(VerificationRow.Compare("profiles_member", eligibleUsers.Count, actualMembers, MigrationRules.MemberProfiles));

        var actualArchived = await PgScalarAsync(context, "SELECT COUNT(*) FROM profile WHERE kind = 'archived'", cancellationToken);
        report.Add(VerificationRow.Info("profiles_archived", actualArchived, MigrationRules.ArchivedProfiles));

        var actualSystem = await PgScalarAsync(context, "SELECT COUNT(*) FROM profile WHERE kind = 'system'", cancellationToken);
        report.Add(VerificationRow.Info("profiles_system", actualSystem, MigrationRules.SystemProfiles));

        await AddEmailLikeProfileRowsAsync(context, report, cancellationToken);
        await AddAvatarGravatarProfileRowsAsync(context, report, cancellationToken);
        await AddEmailLikeGuestNameRowsAsync(context, report, cancellationToken);

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
            "identities_auth0", expectedAuth0.Count, actualByProvider.GetValueOrDefault("auth0"), MigrationRules.LegacyIdentities));
        report.Add(VerificationRow.Compare(
            "identities_legacy_openid", expectedOpenId.Count, actualByProvider.GetValueOrDefault("legacy-openid"), MigrationRules.LegacyIdentities));
        report.Add(VerificationRow.Compare(
            "identities_email", expectedEmail.Count, actualByProvider.GetValueOrDefault("email"), MigrationRules.LegacyIdentities));
        report.Add(VerificationRow.Info(
            "identities_oidc", actualByProvider.GetValueOrDefault("oidc"), MigrationRules.OidcIdentities));
    }

    private sealed record EligibleUser(int Uid, string? OpenId, string? Email);

    // An email address is private, so no public profile field may hold one. The check reads every
    // profile and applies the same address shape the migration avoids.
    private static async Task AddEmailLikeProfileRowsAsync(
        EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        long usernames = 0;
        long displayNames = 0;
        await using (var command = new NpgsqlCommand(
            "SELECT username, display_name FROM profile", context.Target, context.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (EmailLike.Contains(reader.GetString(0)))
                {
                    usernames++;
                }

                if (!reader.IsDBNull(1) && EmailLike.Contains(reader.GetString(1)))
                {
                    displayNames++;
                }
            }
        }

        report.Add(VerificationRow.Compare("profile_username_email_like", 0, usernames, MigrationRules.PrivateAddresses));
        report.Add(VerificationRow.Compare("profile_display_name_email_like", 0, displayNames, MigrationRules.PrivateAddresses));
    }

    // A Gravatar avatar URL is a reversible hash of a member's email address, so no profile may
    // hold one as its external avatar URL. The check reads every profile's external avatar URL
    // and applies the same host rule the migration avoids.
    private static async Task AddAvatarGravatarProfileRowsAsync(
        EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        long gravatarAvatars = 0;
        await using (var command = new NpgsqlCommand(
            "SELECT external_avatar_url FROM profile WHERE external_avatar_url IS NOT NULL", context.Target, context.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (ExternalAvatarUrl.IsGravatar(reader.GetString(0)))
                {
                    gravatarAvatars++;
                }
            }
        }

        report.Add(VerificationRow.Compare("profile_avatar_gravatar", 0, gravatarAvatars, MigrationRules.GravatarAvatars));
    }

    // A guest's poster name is public, so no comment or forum post may hold an address in it. The
    // info row reports how many distinct addresses in legacy guest names got a generated guest name.
    private static async Task AddEmailLikeGuestNameRowsAsync(
        EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "comment", "discussion_post" })
        {
            long emailLike = 0;
            await using (var command = new NpgsqlCommand(
                $"SELECT poster_name FROM {table} WHERE poster_name IS NOT NULL", context.Target, context.Transaction))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (EmailLike.Contains(reader.GetString(0)))
                    {
                        emailLike++;
                    }
                }
            }

            report.Add(VerificationRow.Compare($"{table}_poster_name_email_like", 0, emailLike, MigrationRules.PrivateAddresses));
        }

        var guestNames = await context.GetGuestNamesAsync(cancellationToken);
        report.Add(VerificationRow.Info("guest_names_generated", guestNames.Count, MigrationRules.GuestNames));
    }

    // === tanks ======================================================================================

    private static async Task AddTankRowsAsync(
        EtlContext context, VerificationReport report, long expected, CancellationToken cancellationToken)
    {
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM tank", cancellationToken);
        report.Add(VerificationRow.Compare("tanks", expected, actual, MigrationRules.Tanks));

        var inhabitants = await PgScalarAsync(context, "SELECT COUNT(*) FROM inhabitant", cancellationToken);
        report.Add(VerificationRow.Info("tank_inhabitants", inhabitants, MigrationRules.TankInhabitants));
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
        report.Add(VerificationRow.Compare("discussion_thread", rootIds.Count, actualThreads, MigrationRules.DiscussionThreads));

        var actualPosts = await PgScalarAsync(context, "SELECT COUNT(*) FROM discussion_post", cancellationToken);
        report.Add(VerificationRow.Compare("discussion_post", eligiblePostIds.Count, actualPosts, MigrationRules.DiscussionPosts));

        var postCountMismatches = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM discussion_thread t
            LEFT JOIN (SELECT thread_id, COUNT(*) AS c FROM discussion_post GROUP BY thread_id) p
                ON p.thread_id = t.id
            WHERE COALESCE(p.c, 0) <> t.post_count
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("discussion_thread_post_count_consistency", 0, postCountMismatches, MigrationRules.DiscussionPostCounts));

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
        report.Add(VerificationRow.Compare("media_originals", expectedOriginals, actualOriginals, MigrationRules.MediaOriginals));

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
        report.Add(VerificationRow.Compare("media_forum_attachments", expectedAttachments, actualAttachments, MigrationRules.ForumAttachments));
    }

    // === post (blog posts derived from pictures) ===================================================

    private static async Task AddPostRowsAsync(
        EtlContext context, VerificationReport report, long expected, CancellationToken cancellationToken)
    {
        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM post", cancellationToken);
        report.Add(VerificationRow.Compare("post", expected, actual, MigrationRules.Posts));

        await using var command = new NpgsqlCommand(
            "SELECT state, COUNT(*) FROM post GROUP BY state", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var byState = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            byState[reader.GetString(0)] = reader.GetInt64(1);
        }

        report.Add(VerificationRow.Info("post_state_published", byState.GetValueOrDefault("published"), MigrationRules.PostStates));
        report.Add(VerificationRow.Info("post_state_draft", byState.GetValueOrDefault("draft"), MigrationRules.PostStates));
        report.Add(VerificationRow.Info("post_state_archived", byState.GetValueOrDefault("archived"), MigrationRules.PostStates));
    }

    // === post_species (picture-species links) =======================================================

    /// <summary>
    /// Recomputes SpeciesLinksStep's eligibility formula (a link with a nonzero species reference
    /// whose picture is eligible as a post and whose species is eligible for the catalog) as the
    /// count of distinct (post, species) pairs directly from the legacy source, and checks it
    /// against post_species.
    /// </summary>
    private static async Task AddPostSpeciesRowsAsync(
        EtlContext context,
        VerificationReport report,
        Dictionary<int, int> postOwnerByPictureUid,
        HashSet<int> speciesUids,
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
                if (postOwnerByPictureUid.ContainsKey(postLegacyId) && speciesUids.Contains(speciesLegacyId))
                {
                    distinctPairs.Add((postLegacyId, speciesLegacyId));
                }
            }
        }

        var actual = await PgScalarAsync(context, "SELECT COUNT(*) FROM post_species", cancellationToken);
        report.Add(VerificationRow.Compare("post_species", distinctPairs.Count, actual, MigrationRules.PostSpecies));
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
        report.Add(VerificationRow.Compare("slug_alias_one_canonical_per_post", 0, canonicalViolations, MigrationRules.SlugAliases));

        var total = await PgScalarAsync(context, "SELECT COUNT(*) FROM slug_alias", cancellationToken);
        report.Add(VerificationRow.Info("slug_alias_total", total, MigrationRules.SlugAliases));

        var orphans = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM slug_alias sa LEFT JOIN post p ON p.id = sa.post_id WHERE p.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("slug_alias_orphans", 0, orphans, MigrationRules.SlugAliases));

        var uniqueViolations = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM (SELECT value FROM slug_alias GROUP BY value HAVING COUNT(*) > 1) x",
            cancellationToken);
        report.Add(VerificationRow.Compare("slug_alias_unique_value", 0, uniqueViolations, MigrationRules.SlugAliases));

        var postsWithoutAlias = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM post p LEFT JOIN slug_alias sa ON sa.post_id = p.id WHERE sa.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("post_without_slug_alias", 0, postsWithoutAlias, MigrationRules.SlugAliases));
    }

    // === comment, rating and archive.legacy_comment (vault) =========================================

    /// <summary>
    /// Recomputes CommentMigrationStep's per-row classification (a target outside the eligible
    /// posts and tanks vaults the row; an eligible target with no text and no rating is dropped;
    /// otherwise a non-empty note becomes a comment and a positive rating becomes a rating
    /// candidate, independently of each other) by streaming the whole legacy comments table, and
    /// checks the resulting counts against comment, rating and archive.legacy_comment. A rating
    /// candidate counts once per member and target; a member rating the picture or tank they own
    /// in the legacy source is a self-rating and an anonymous rating always counts. Checks that
    /// every migrated rating is an anonymous rating or the newest rating of its member on its
    /// target, a tie going to the higher legacy uid. Reports the collapsed and the self-ratings as
    /// information. Returns the legacy ids of comments that
    /// migrated, for the vote check.
    /// </summary>
    private static async Task<HashSet<int>> AddCommentRatingVaultRowsAsync(
        EtlContext context,
        VerificationReport report,
        Dictionary<int, int> postOwnerByPictureUid,
        Dictionary<int, int> tankOwnerByTankUid,
        CancellationToken cancellationToken)
    {
        var vaultCount = 0L;
        var commentCount = 0L;
        var anonymousRatingUids = new HashSet<int>();
        var memberRatings = 0L;
        var selfRatings = 0L;
        var newestRatingByMemberAndTarget = new Dictionary<(int FeUser, int Type, int Item), (long CreatedAt, int Uid)>();
        var migratedCommentLegacyIds = new HashSet<int>();

        await using (var command = new MySqlCommand(
            "SELECT uid, type, item, rating, note, deleted, fe_user, crdate, tstamp FROM user_cichlids_comments",
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

                var owners = type switch
                {
                    1 => postOwnerByPictureUid,
                    2 => tankOwnerByTankUid,
                    _ => null,
                };

                if (owners is null || !owners.TryGetValue(item, out var ownerFeUser))
                {
                    // Every row without an eligible target lands in the vault, whatever the reason
                    // (a deleted target, a target id that never existed, or an unrecognized type).
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

                if (!hasRating)
                {
                    continue;
                }

                var feUser = reader.GetInt32(6);
                if (feUser == 0)
                {
                    anonymousRatingUids.Add(legacyId);
                }
                else if (feUser == ownerFeUser)
                {
                    selfRatings++;
                }
                else
                {
                    memberRatings++;
                    var crdate = reader.GetInt64(7);
                    var candidate = (crdate > 0 ? crdate : reader.GetInt64(8), legacyId);
                    var key = (feUser, type, item);
                    if (!newestRatingByMemberAndTarget.TryGetValue(key, out var newest)
                        || candidate.CompareTo(newest) > 0)
                    {
                        newestRatingByMemberAndTarget[key] = candidate;
                    }
                }
            }
        }

        var actualComments = await PgScalarAsync(context, "SELECT COUNT(*) FROM comment", cancellationToken);
        report.Add(VerificationRow.Compare("comment", commentCount, actualComments, MigrationRules.Comments));

        var actualRatings = await PgScalarAsync(context, "SELECT COUNT(*) FROM rating", cancellationToken);
        report.Add(VerificationRow.Compare(
            "rating", anonymousRatingUids.Count + newestRatingByMemberAndTarget.Count, actualRatings, MigrationRules.Ratings));

        var expectedRatingUids = new HashSet<int>(anonymousRatingUids);
        expectedRatingUids.UnionWith(newestRatingByMemberAndTarget.Values.Select(newest => newest.Uid));
        var ratingsNotNewest = 0L;
        await using (var command = new NpgsqlCommand(
            "SELECT legacy_comment_id FROM rating", context.Target, context.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || !expectedRatingUids.Contains(reader.GetInt32(0)))
                {
                    ratingsNotNewest++;
                }
            }
        }

        report.Add(VerificationRow.Compare("rating_not_newest", 0, ratingsNotNewest, MigrationRules.NewestRatings));
        report.Add(VerificationRow.Info(
            "rating_collapsed", memberRatings - newestRatingByMemberAndTarget.Count, MigrationRules.CollapsedRatings));
        report.Add(VerificationRow.Info("rating_self_dropped", selfRatings, MigrationRules.SelfRatings));

        var actualVault = await PgScalarAsync(context, "SELECT COUNT(*) FROM archive.legacy_comment", cancellationToken);
        report.Add(VerificationRow.Compare("vault_legacy_comment", vaultCount, actualVault, MigrationRules.LegacyCommentVault));

        return migratedCommentLegacyIds;
    }

    // === comment_vote ===============================================================================

    /// <summary>
    /// Recomputes MigrateCommentVotesAsync's dedupe (the earliest vote per comment/voter pair wins)
    /// and its two drop rules (an invalid value, or a comment that never migrated) from the legacy
    /// votes table, and checks the result against comment_vote. Also checks that no vote is older
    /// than its comment.
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
        report.Add(VerificationRow.Compare("comment_vote", expectedVotes, actual, MigrationRules.CommentVotes));

        var votesBeforeComment = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM comment_vote v JOIN comment c ON c.id = v.comment_id WHERE v.created_at < c.created_at",
            cancellationToken);
        report.Add(VerificationRow.Compare(
            "comment_vote_not_before_comment", 0, votesBeforeComment, MigrationRules.VoteTimes));
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
        report.Add(VerificationRow.Compare("post_rating_count_consistency", 0, ratingCountMismatches, MigrationRules.Aggregates));

        var scoreMismatches = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM comment c
            LEFT JOIN (SELECT comment_id, SUM(value) AS s FROM comment_vote GROUP BY comment_id) v
                ON v.comment_id = c.id
            WHERE COALESCE(v.s, 0) <> c.score
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("comment_score_consistency", 0, scoreMismatches, MigrationRules.Aggregates));
    }

    // === collection and collection_entry (galleries) ================================================

    private static async Task AddCollectionRowsAsync(
        EtlContext context, VerificationReport report, Dictionary<int, int> postOwnerByPictureUid, CancellationToken cancellationToken)
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
        report.Add(VerificationRow.Compare("collection", expectedCollections, actualCollections, MigrationRules.Collections));

        var emptyGalleries = await MySqlScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM user_cichlids_gallery g
            WHERE g.deleted = 0
              AND NOT EXISTS (SELECT 1 FROM user_cichlids_gallery_pictures_mm mm WHERE mm.uid_gallery = g.uid)
            """,
            cancellationToken);
        report.Add(VerificationRow.Info("collection_skipped_empty", emptyGalleries, MigrationRules.EmptyGalleries));

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
                if (postOwnerByPictureUid.ContainsKey(picture))
                {
                    distinctEntries.Add((reader.GetInt32(0), picture));
                }
            }
        }

        var actualEntries = await PgScalarAsync(context, "SELECT COUNT(*) FROM collection_entry", cancellationToken);
        report.Add(VerificationRow.Compare("collection_entry", distinctEntries.Count, actualEntries, MigrationRules.CollectionEntries));
    }

    // === reference and domain checks (target only) ==================================================

    private static async Task AddReferenceCheckRowsAsync(EtlContext context, VerificationReport report, CancellationToken cancellationToken)
    {
        var postsWithoutMedia = await PgScalarAsync(
            context,
            "SELECT COUNT(*) FROM post p LEFT JOIN post_media pm ON pm.post_id = p.id WHERE pm.id IS NULL",
            cancellationToken);
        report.Add(VerificationRow.Compare("post_without_media", 0, postsWithoutMedia, MigrationRules.PostMedia));

        var publishedWithoutCanonicalSlug = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM post p
            WHERE p.state = 'published'
              AND NOT EXISTS (SELECT 1 FROM slug_alias sa WHERE sa.post_id = p.id AND sa.is_canonical)
            """,
            cancellationToken);
        report.Add(VerificationRow.Compare("post_published_without_canonical_slug", 0, publishedWithoutCanonicalSlug, MigrationRules.SlugAliases));

        var archivedWithMemberAuthor = await PgScalarAsync(
            context,
            """
            SELECT COUNT(*) FROM post p
            JOIN profile pr ON pr.id = p.author_profile_id
            WHERE p.state = 'archived' AND pr.kind = 'member'
            """,
            cancellationToken);
        report.Add(VerificationRow.Info("post_archived_author_member", archivedWithMemberAuthor, MigrationRules.PostStates));
    }

    // === shared helpers ==============================================================================

    /// <summary>
    /// Reads a legacy query returning a uid and an owner fe_user per row into a map from uid to
    /// owner.
    /// </summary>
    private static async Task<Dictionary<int, int>> LoadLegacyOwnerMapAsync(
        EtlContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return result;
    }

    private static async Task<HashSet<int>> LoadLegacyUidSetAsync(
        EtlContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, context.Legacy);
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
