using System.Security.Cryptography;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Identity;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates the legacy discussion forum (Phorum, database <c>cichlids_phorum5</c> on the same
/// MySQL server as the TYPO3 legacy database) into the platform's own discussion archive entities
/// (ADR-0021): visible messages only, threads rebuilt from each root message, authors matched to
/// migrated profiles by e-mail or attributed to a forum-specific placeholder profile, and message
/// attachments exported from their inline base64 storage into the object store as media items.
/// Legacy e-mail addresses and private messages never migrate.
/// </summary>
public sealed class ForumMigrationStep : IEtlStep
{
    // The Phorum database lives on the same MySQL server and account as the TYPO3 legacy
    // database EtlContext.Legacy already connects to, so every query below reaches it through a
    // fully qualified table name on that same connection rather than a second one. Both
    // databases' text columns carry their own charset metadata (latin1 here), which MySqlConnector
    // decodes per column regardless of the connection's own charset, so no special handling is
    // needed to read this database's German-language content correctly.
    internal const string PhorumDatabase = "cichlids_phorum5";

    private const int ThreadBatchSize = 2000;
    private const int PostBatchSize = 2000;

    // 2003-01-01 UTC: the same "unknown creation moment" marker every other step uses for a
    // zero legacy timestamp, deliberately earlier than the site's actual launch so it reads as
    // "unknown" rather than as a plausible date.
    private static readonly DateTimeOffset UnknownCreatedAtMarker = new(2003, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<int, DiscussionCategory> CategoryByForumId = new()
    {
        [1] = DiscussionCategory.Cichlids,
        [2] = DiscussionCategory.African,
        [3] = DiscussionCategory.MarketPlace,
    };

    /// <summary>
    /// The Phorum forum ids whose messages migrate.
    /// </summary>
    internal static IEnumerable<int> MigratedForumIds => CategoryByForumId.Keys;

    private static readonly ValueConverter<DiscussionCategory, string> CategoryConverter = new SnakeCaseEnumConverter<DiscussionCategory>();
    private static readonly ValueConverter<DiscussionThreadState, string> StateConverter = new SnakeCaseEnumConverter<DiscussionThreadState>();
    private static readonly ValueConverter<ProfileKind, string> ProfileKindConverter = new SnakeCaseEnumConverter<ProfileKind>();
    private static readonly ValueConverter<MediaKind, string> MediaKindConverter = new SnakeCaseEnumConverter<MediaKind>();

    public string Name => "forum";

    public int Order => 70;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        if (context.ObjectStore is null)
        {
            throw new InvalidOperationException(
                "The forum step exports attachments into object storage; configure the \"ObjectStorage\" section before running it.");
        }

        var stats = context.Statistics.ForStep(Name);

        var emailToProfileId = await LoadProfileEmailIndexAsync(context, cancellationToken);
        var phorumUsers = await LoadPhorumUsersAsync(context, cancellationToken);
        var forumUserProfileCache = new Dictionary<int, long>();
        var guestNames = await context.GetGuestNamesAsync(cancellationToken);

        var messages = await LoadVisibleMessagesAsync(context, stats, cancellationToken);

        var threadIdByLegacyId = await MigrateThreadsAsync(context, messages, stats, cancellationToken);

        var (postIdByLegacyId, authorProfileIdByLegacyId) = await MigratePostsAsync(
            context, messages, threadIdByLegacyId, phorumUsers, emailToProfileId, forumUserProfileCache, guestNames, stats, cancellationToken);

        await RemoveStalePostsAsync(context, postIdByLegacyId.Keys, cancellationToken);
        await RemoveStaleThreadsAsync(context, threadIdByLegacyId.Keys, cancellationToken);

        await MigrateAttachmentsAsync(context, postIdByLegacyId, authorProfileIdByLegacyId, stats, cancellationToken);

        await RecomputeThreadAggregatesAsync(context, cancellationToken);
    }

    private static async Task<Dictionary<int, long>> MigrateThreadsAsync(
        EtlContext context, IReadOnlyList<RawMessage> messages, StepStatistics stats, CancellationToken cancellationToken)
    {
        var threadIdByLegacyId = new Dictionary<int, long>();
        var roots = messages.Where(m => m.ParentId == 0 && m.MessageId == m.Thread).ToList();

        foreach (var chunk in Chunk(roots, ThreadBatchSize))
        {
            var rows = new List<IReadOnlyList<object?>>(chunk.Count);
            foreach (var root in chunk)
            {
                var title = TrimOrNull(root.Subject) ?? $"Thread {root.MessageId}";
                var createdAt = root.Datestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(root.Datestamp) : UnknownCreatedAtMarker;

                rows.Add(new object?[]
                {
                    root.MessageId,
                    CategoryConverter.ConvertToProvider(CategoryByForumId[root.ForumId]),
                    title,
                    StateConverter.ConvertToProvider(DiscussionThreadState.Archived),
                    createdAt,
                });
            }

            var results = await PgBatchUpsert.UpsertBatchAsync(
                context.Target,
                context.Transaction,
                "discussion_thread",
                "legacy_id",
                ["legacy_id", "category", "title", "state", "created_at"],
                rows,
                cancellationToken);

            foreach (var (legacyId, result) in results)
            {
                threadIdByLegacyId[legacyId] = result.Id;
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

        return threadIdByLegacyId;
    }

    /// <summary>
    /// Deletes every discussion_post row whose legacy id is not among this run's migrated
    /// messages, together with its discussion_post_media rows through the foreign key cascade, and
    /// any forum-attachment media_item row left with no discussion_post_media referencing it.
    /// </summary>
    private static async Task RemoveStalePostsAsync(
        EtlContext context, IReadOnlyCollection<int> currentPostLegacyIds, CancellationToken cancellationToken)
    {
        await using (var deletePosts = new NpgsqlCommand(
            """
            DELETE FROM discussion_post
            WHERE legacy_id IS NOT NULL AND legacy_id <> ALL(@postLegacyIds)
            """,
            context.Target, context.Transaction))
        {
            deletePosts.Parameters.AddWithValue("postLegacyIds", currentPostLegacyIds.ToArray());
            await deletePosts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var deleteOrphanedMedia = new NpgsqlCommand(
            """
            DELETE FROM media_item
            WHERE storage_key LIKE 'forum_attachments/%'
              AND NOT EXISTS (SELECT 1 FROM discussion_post_media WHERE discussion_post_media.media_item_id = media_item.id)
            """,
            context.Target, context.Transaction);
        await deleteOrphanedMedia.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes every discussion_thread row whose legacy id is not among this run's roots and that
    /// carries no posts.
    /// </summary>
    private static async Task RemoveStaleThreadsAsync(
        EtlContext context, IReadOnlyCollection<int> currentRootLegacyIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM discussion_thread
            WHERE legacy_id IS NOT NULL
              AND legacy_id <> ALL(@rootLegacyIds)
              AND NOT EXISTS (SELECT 1 FROM discussion_post WHERE discussion_post.thread_id = discussion_thread.id)
            """,
            context.Target, context.Transaction);
        command.Parameters.AddWithValue("rootLegacyIds", currentRootLegacyIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Builds every migrated message's discussion_post row, sorted within its thread by
    /// (datestamp, message_id), and returns the message_id -> post id and message_id -> resolved
    /// author profile id maps the attachment pass needs. A message whose thread column resolves to
    /// no migrated thread (its original root message was pruned entirely rather than merely
    /// hidden) is skipped rather than migrated.
    /// </summary>
    private static async Task<(Dictionary<int, long> PostIdByLegacyId, Dictionary<int, long?> AuthorProfileIdByLegacyId)> MigratePostsAsync(
        EtlContext context,
        IReadOnlyList<RawMessage> messages,
        Dictionary<int, long> threadIdByLegacyId,
        Dictionary<int, PhorumUser> phorumUsers,
        Dictionary<string, long> emailToProfileId,
        Dictionary<int, long> forumUserProfileCache,
        GuestNames guestNames,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        var postsByThread = new Dictionary<long, List<(RawMessage Message, long? AuthorProfileId, string? PosterName)>>();

        foreach (var message in messages)
        {
            if (!threadIdByLegacyId.TryGetValue(message.Thread, out var threadId))
            {
                stats.AddSkip("forum_post_thread_root_missing");
                continue;
            }

            var (authorProfileId, posterName) = await ResolveAuthorAsync(
                context, message.UserId, message.Author, phorumUsers, emailToProfileId, forumUserProfileCache, guestNames, stats, cancellationToken);

            if (!postsByThread.TryGetValue(threadId, out var list))
            {
                list = [];
                postsByThread[threadId] = list;
            }

            list.Add((message, authorProfileId, posterName));
        }

        var postIdByLegacyId = new Dictionary<int, long>();
        var authorProfileIdByLegacyId = new Dictionary<int, long?>();
        var allRows = new List<IReadOnlyList<object?>>();

        foreach (var (_, entries) in postsByThread)
        {
            var ordered = entries.OrderBy(e => e.Message.Datestamp).ThenBy(e => e.Message.MessageId).ToList();
            for (var sort = 0; sort < ordered.Count; sort++)
            {
                var (message, authorProfileId, posterName) = ordered[sort];
                var createdAt = message.Datestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Datestamp) : UnknownCreatedAtMarker;

                allRows.Add(new object?[]
                {
                    message.MessageId,
                    threadIdByLegacyId[message.Thread],
                    authorProfileId,
                    posterName,
                    message.Body.TrimEnd(),
                    createdAt,
                    sort,
                });

                authorProfileIdByLegacyId[message.MessageId] = authorProfileId;
            }
        }

        foreach (var chunk in Chunk(allRows, PostBatchSize))
        {
            var results = await PgBatchUpsert.UpsertBatchAsync(
                context.Target,
                context.Transaction,
                "discussion_post",
                "legacy_id",
                ["legacy_id", "thread_id", "author_profile_id", "poster_name", "body", "created_at", "sort"],
                chunk,
                cancellationToken);

            foreach (var (legacyId, result) in results)
            {
                postIdByLegacyId[legacyId] = result.Id;
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

        return (postIdByLegacyId, authorProfileIdByLegacyId);
    }

    /// <summary>
    /// Resolves a forum message's author: a guest (user_id 0) has no profile and only a public
    /// poster name (its display name, or its generated guest name when the display name contains
    /// an email address), a registered forum user whose e-mail matches a migrated profile's
    /// identity is attributed to that profile, and every other registered forum user gets a
    /// forum-specific placeholder profile (ADR-0021), cached per phorum user id so repeat posts and
    /// the warning about the missing match cost one resolution, not one per post.
    /// </summary>
    private static async Task<(long? ProfileId, string? PosterName)> ResolveAuthorAsync(
        EtlContext context,
        int userId,
        string? author,
        Dictionary<int, PhorumUser> phorumUsers,
        Dictionary<string, long> emailToProfileId,
        Dictionary<int, long> forumUserProfileCache,
        GuestNames guestNames,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        if (userId == 0)
        {
            return (null, guestNames.Resolve(TrimOrNull(author)));
        }

        if (forumUserProfileCache.TryGetValue(userId, out var cachedProfileId))
        {
            return (cachedProfileId, null);
        }

        phorumUsers.TryGetValue(userId, out var phorumUser);

        if (phorumUser?.Email is { } email && emailToProfileId.TryGetValue(email, out var matchedProfileId))
        {
            forumUserProfileCache[userId] = matchedProfileId;
            return (matchedProfileId, null);
        }

        var placeholderId = await EnsureForumPlaceholderProfileAsync(context, userId, phorumUser?.DisplayName, cancellationToken);
        forumUserProfileCache[userId] = placeholderId;
        stats.AddWarning($"forum author phorum user {userId} has no e-mail match to a migrated profile; created a forum placeholder profile.");
        return (placeholderId, null);
    }

    /// <summary>
    /// Finds or creates the archived placeholder profile for one specific phorum user id.
    /// Idempotent over <c>username</c> rather than <c>legacy_id</c>: phorum user ids and TYPO3
    /// <c>fe_users</c> ids are separate numbering spaces sharing no meaning, so keying this on
    /// <c>legacy_id</c> the way <see cref="PlaceholderProfiles"/> does for TYPO3 users could
    /// collide two unrelated legacy accounts onto the same profile row.
    /// </summary>
    private static async Task<long> EnsureForumPlaceholderProfileAsync(
        EtlContext context, int phorumUserId, string? phorumDisplayName, CancellationToken cancellationToken)
    {
        // A display name is public, so an e-mail address never becomes one.
        var trimmed = TrimOrNull(phorumDisplayName);
        var displayName = PublicHandle.IsUsable(trimmed) ? trimmed! : "Former member";

        var values = new (string, object?)[]
        {
            ("username", $"forum-member-{phorumUserId}"),
            ("display_name", displayName),
            ("kind", ProfileKindConverter.ConvertToProvider(ProfileKind.Archived)),
            ("created_at", DateTimeOffset.UtcNow),
        };

        var (profileId, _) = await PgUpsert.UpsertAsync(context.Target, context.Transaction, "profile", "username", values, cancellationToken);
        return profileId;
    }

    /// <summary>
    /// Exports every migrated post's attachments (<c>phorum_files</c> rows linked to a message)
    /// into the object store as media items. Idempotent per file id through the deterministic
    /// storage key: a second run with an unchanged decoded byte size skips the re-upload but still
    /// keeps the media_item and discussion_post_media rows in sync.
    /// </summary>
    private static async Task MigrateAttachmentsAsync(
        EtlContext context,
        Dictionary<int, long> postIdByLegacyId,
        Dictionary<int, long?> authorProfileIdByLegacyId,
        StepStatistics stats,
        CancellationToken cancellationToken)
    {
        var existingByteSizeByStorageKey = await LoadExistingAttachmentByteSizesAsync(context, cancellationToken);

        const string sql = $"""
            SELECT file_id, filename, file_data, message_id
            FROM {PhorumDatabase}.phorum_files
            WHERE link = 'message' AND message_id > 0
            ORDER BY file_id
            """;

        var mediaRows = new List<IReadOnlyList<object?>>();
        var attachments = new List<(string StorageKey, int PostLegacyId, int Sort)>();
        var nextSortByPostLegacyId = new Dictionary<int, int>();
        var uploaded = 0;
        var uploadsSkippedUnchanged = 0;

        await using (var command = new MySqlCommand(sql, context.Legacy))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var fileId = reader.GetInt32("file_id");
                var messageId = reader.GetInt32("message_id");

                if (!postIdByLegacyId.ContainsKey(messageId))
                {
                    stats.AddSkip("attachment_post_not_migrated");
                    continue;
                }

                var filename = reader.GetStringOrEmpty("filename").Trim();
                var base64 = reader.GetStringOrEmpty("file_data");

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(base64);
                }
                catch (FormatException)
                {
                    stats.AddSkip("attachment_invalid_base64");
                    continue;
                }

                var storageKey = $"forum_attachments/{fileId}_{StorageKeyNormalizer.NormalizeFilename(filename)}";
                var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var ownerProfileId = authorProfileIdByLegacyId.GetValueOrDefault(messageId);

                if (existingByteSizeByStorageKey.TryGetValue(storageKey, out var existingByteSize) && existingByteSize == bytes.LongLength)
                {
                    uploadsSkippedUnchanged++;
                }
                else
                {
                    await using var content = new MemoryStream(bytes);
                    await context.ObjectStore!.PutAsync(storageKey, content, ResolveContentType(filename), cancellationToken);
                    uploaded++;
                }

                mediaRows.Add(new object?[]
                {
                    ownerProfileId,
                    MediaKindConverter.ConvertToProvider(MediaKind.Photo),
                    storageKey,
                    filename.Length == 0 ? null : filename,
                    bytes.LongLength,
                    checksum,
                    DateTimeOffset.UtcNow,
                });

                var sort = nextSortByPostLegacyId.GetValueOrDefault(messageId);
                nextSortByPostLegacyId[messageId] = sort + 1;
                attachments.Add((storageKey, messageId, sort));
            }
        }

        stats.AddWarning($"forum attachments: {uploaded} uploaded, {uploadsSkippedUnchanged} skipped (unchanged size).");

        if (mediaRows.Count == 0)
        {
            return;
        }

        var mediaIdByStorageKey = new Dictionary<string, long>();
        foreach (var chunk in Chunk(mediaRows, PostBatchSize))
        {
            var results = await UpsertMediaItemsByStorageKeyAsync(context, chunk, cancellationToken);
            foreach (var (storageKey, (id, inserted)) in results)
            {
                mediaIdByStorageKey[storageKey] = id;
                if (inserted)
                {
                    stats.AddInserted();
                }
                else
                {
                    stats.AddUpdated();
                }
            }
        }

        var postMediaRows = new List<IReadOnlyList<object?>>(attachments.Count);
        foreach (var (storageKey, postLegacyId, sort) in attachments)
        {
            postMediaRows.Add(new object?[] { postIdByLegacyId[postLegacyId], mediaIdByStorageKey[storageKey], sort });
        }

        await PgBulkInsert.InsertOnConflictDoNothingAsync(
            context.Target,
            context.Transaction,
            "discussion_post_media",
            ["discussion_post_id", "media_item_id"],
            ["discussion_post_id", "media_item_id", "sort"],
            postMediaRows,
            cancellationToken);
    }

    private static async Task<Dictionary<string, (long Id, bool Inserted)>> UpsertMediaItemsByStorageKeyAsync(
        EtlContext context, IReadOnlyList<IReadOnlyList<object?>> rows, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, (long Id, bool Inserted)>();

        // media_item.legacy_id is already claimed by the picture-migration TYPO3 uid namespace;
        // a phorum file id could collide with an unrelated picture's legacy id, so attachment
        // media items upsert on their deterministic storage_key instead and leave legacy_id null.
        foreach (var row in rows)
        {
            var values = new (string, object?)[]
            {
                ("owner_profile_id", row[0]),
                ("kind", row[1]),
                ("storage_key", row[2]),
                ("original_filename", row[3]),
                ("byte_size", row[4]),
                ("checksum_sha256", row[5]),
                ("created_at", row[6]),
            };

            var (id, inserted) = await PgUpsert.UpsertAsync(context.Target, context.Transaction, "media_item", "storage_key", values, cancellationToken);
            results[(string)row[2]!] = (id, inserted);
        }

        return results;
    }

    private static async Task<Dictionary<string, long?>> LoadExistingAttachmentByteSizesAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT storage_key, byte_size FROM media_item WHERE storage_key LIKE 'forum_attachments/%'",
            context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<string, long?>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        }

        return result;
    }

    private static async Task RecomputeThreadAggregatesAsync(EtlContext context, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE discussion_thread AS t SET post_count = agg.cnt, last_post_at = agg.last_post_at
            FROM (
                SELECT thread_id, COUNT(*) AS cnt, MAX(created_at) AS last_post_at
                FROM discussion_post
                GROUP BY thread_id
            ) AS agg
            WHERE t.id = agg.thread_id
            """,
            context.Target, context.Transaction);

        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveContentType(string filename)
    {
        var lastDot = filename.LastIndexOf('.');
        var extension = lastDot < 0 ? string.Empty : filename[(lastDot + 1)..].ToLowerInvariant();

        return extension switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "png" => "image/png",
            _ => "application/octet-stream",
        };
    }

    private static async Task<List<RawMessage>> LoadVisibleMessagesAsync(
        EtlContext context, StepStatistics stats, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT message_id, forum_id, thread, parent_id, author, subject, body, user_id, datestamp, status, moved
            FROM {PhorumDatabase}.phorum_messages
            ORDER BY message_id
            """;

        var result = new List<RawMessage>();

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            stats.AddRead();

            var status = reader.GetInt32("status");
            if (status != 2)
            {
                stats.AddSkip("forum_message_hidden_status");
                continue;
            }

            if (reader.GetBoolean("moved"))
            {
                // A Phorum "thread moved" notice: a stub message Phorum drops in the forum a
                // thread moved out of, pointing at the real thread through its own thread column.
                // It is neither a thread of its own nor a post of the thread it points at.
                stats.AddSkip("forum_message_moved_notice");
                continue;
            }

            var forumId = reader.GetInt32("forum_id");
            if (!CategoryByForumId.ContainsKey(forumId))
            {
                stats.AddSkip("forum_message_forum_unmapped");
                continue;
            }

            result.Add(new RawMessage(
                reader.GetInt32("message_id"),
                forumId,
                reader.GetInt32("thread"),
                reader.GetInt32("parent_id"),
                reader.GetTrimmedOrNull("author"),
                reader.GetTrimmedOrNull("subject"),
                reader.GetStringOrEmpty("body"),
                reader.GetInt32("user_id"),
                reader.GetInt64("datestamp")));
        }

        return result;
    }

    private static async Task<Dictionary<string, long>> LoadProfileEmailIndexAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT subject, profile_id FROM profile_identity WHERE provider = 'email'", context.Target, context.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0).ToLowerInvariant()] = reader.GetInt64(1);
        }

        return result;
    }

    private static async Task<Dictionary<int, PhorumUser>> LoadPhorumUsersAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        const string sql = $"SELECT user_id, email, display_name FROM {PhorumDatabase}.phorum_users";

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, PhorumUser>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var email = reader.GetTrimmedOrNull("email")?.ToLowerInvariant();
            var displayName = reader.GetTrimmedOrNull("display_name");
            result[reader.GetInt32("user_id")] = new PhorumUser(email, displayName);
        }

        return result;
    }

    private static string? TrimOrNull(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int chunkSize)
    {
        for (var offset = 0; offset < source.Count; offset += chunkSize)
        {
            yield return source.Skip(offset).Take(chunkSize).ToList();
        }
    }

    private sealed record RawMessage(
        int MessageId,
        int ForumId,
        int Thread,
        int ParentId,
        string? Author,
        string? Subject,
        string Body,
        int UserId,
        long Datestamp);

    private sealed record PhorumUser(string? Email, string? DisplayName);
}
