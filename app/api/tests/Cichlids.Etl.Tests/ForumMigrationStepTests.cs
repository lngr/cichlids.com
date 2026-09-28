using System.Security.Cryptography;
using System.Text;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class ForumMigrationStepTests(EtlFixture fixture)
{
    // Matches cichlids_phorum5.phorum_files file_id 9001 in the shared legacy seed: 61 bytes,
    // sha256 c93a6c62ca94d9a9f209f9c99325e015d21443372252f343b7c8a759a00dafe8. The step never parses
    // attachment content as an image, so the fixture bytes only need to round-trip through base64
    // and the object store correctly, not decode as a real JPEG.
    private static readonly byte[] AttachmentBytes = Encoding.ASCII.GetBytes(
        "FAKE-JPEG-BYTES-FOR-FORUM-ETL-ATTACHMENT-TEST-0001-0123456789");

    [Fact]
    public async Task MigratesTheForumIntoTheDiscussionArchiveIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        // Read counts every row phorum_messages actually has: 11 in the fixture (90001-90008,
        // 90010, 90020, 90030). One (90004) is hidden, one (90007) is a moved notice, and two
        // (90008, 90020) have a thread column that resolves to no surviving root; the remaining 7
        // are visible, forum-mapped posts.
        Assert.Equal(11, stats1.Read);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("forum_message_hidden_status"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("forum_message_moved_notice"));
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("forum_post_thread_root_missing"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("attachment_post_not_migrated"));

        await using (var db = fixture.CreateTargetContext())
        {
            var alice = await db.Profiles.SingleAsync(p => p.Username == "forum-fixture-alice-901");

            // Thread A: root 90001 plus five surviving replies (90004 is hidden, 90020's thread
            // never resolves), ordered by datestamp with a message_id tie-break.
            var threadA = await db.DiscussionThreads
                .Include(t => t.Posts)
                .SingleAsync(t => t.LegacyId == 90001);
            Assert.Equal(DiscussionCategory.Cichlids, threadA.Category);
            Assert.Equal("Root Subject A", threadA.Title);
            Assert.Equal(DiscussionThreadState.Archived, threadA.State);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000100), threadA.CreatedAt);
            Assert.Equal(5, threadA.PostCount);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000250), threadA.LastPostAt);

            var postsInOrder = threadA.Posts.OrderBy(p => p.Sort).ToList();
            Assert.Equal([90001, 90003, 90002, 90005, 90006], postsInOrder.Select(p => p.LegacyId));

            var root = postsInOrder[0];
            Assert.Equal(alice.Id, root.AuthorProfileId);
            Assert.Null(root.PosterName);
            Assert.Equal("Root post body with umlaut: Größe.", root.Body);

            var guestReply = postsInOrder[2];
            Assert.Null(guestReply.AuthorProfileId);
            Assert.Equal("GuestPoster", guestReply.PosterName);
            Assert.Equal("Guest reply body.", guestReply.Body);

            var placeholderReply = postsInOrder[1];
            Assert.NotNull(placeholderReply.AuthorProfileId);
            Assert.Null(placeholderReply.PosterName);
            var placeholderProfile = await db.Profiles.SingleAsync(p => p.Id == placeholderReply.AuthorProfileId);
            Assert.Equal("forum-member-902", placeholderProfile.Username);
            Assert.Equal("Bob NoMatch", placeholderProfile.DisplayName);
            Assert.Equal(ProfileKind.Archived, placeholderProfile.Kind);
            Assert.Null(placeholderProfile.LegacyId);

            var unknownUserReply = postsInOrder[3];
            var unknownUserProfile = await db.Profiles.SingleAsync(p => p.Id == unknownUserReply.AuthorProfileId);
            Assert.Equal("forum-member-903", unknownUserProfile.Username);
            Assert.Equal("Former member", unknownUserProfile.DisplayName);

            // Thread C's author 904 has an e-mail address as Phorum display name.
            var threadC = await db.DiscussionThreads.Include(t => t.Posts).SingleAsync(t => t.LegacyId == 90030);
            var addressNamedProfile = await db.Profiles.SingleAsync(p => p.Id == Assert.Single(threadC.Posts).AuthorProfileId);
            Assert.Equal("forum-member-904", addressNamedProfile.Username);
            Assert.Equal("Former member", addressNamedProfile.DisplayName);

            // 90004 (hidden) never appears anywhere.
            Assert.DoesNotContain(postsInOrder, p => p.LegacyId == 90004);
            Assert.False(await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90004));

            // Thread B: the attachment thread.
            var threadB = await db.DiscussionThreads
                .Include(t => t.Posts).ThenInclude(p => p.Media)
                .SingleAsync(t => t.LegacyId == 90010);
            Assert.Equal(DiscussionCategory.African, threadB.Category);
            Assert.Single(threadB.Posts);

            var attachmentPost = threadB.Posts[0];
            var media = Assert.Single(attachmentPost.Media);
            var mediaItem = await db.MediaItems.SingleAsync(m => m.Id == media.MediaItemId);
            Assert.Equal("forum_attachments/9001_attach.jpg", mediaItem.StorageKey);
            Assert.Equal("attach.jpg", mediaItem.OriginalFilename);
            Assert.Equal(MediaKind.Photo, mediaItem.Kind);
            Assert.Equal(AttachmentBytes.LongLength, mediaItem.ByteSize);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(AttachmentBytes)), mediaItem.ChecksumSha256);
            Assert.Equal(alice.Id, mediaItem.OwnerProfileId);
            Assert.Null(mediaItem.LegacyId);

            var stored = await fixture.ObjectStore.GetAsync(mediaItem.StorageKey);
            Assert.NotNull(stored);
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer);
            Assert.Equal(AttachmentBytes, buffer.ToArray());

            // 90020 is a reply (parent_id 90015) whose thread column (90099) has no surviving
            // root anywhere in the fixture, so it was skipped rather than migrated.
            Assert.False(await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90020));

            // 90007 (moved notice) neither became a thread nor a post of thread A.
            Assert.False(await db.DiscussionThreads.AnyAsync(t => t.LegacyId == 90007));
            Assert.False(await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90007));

            // 90008 (parent_id 0, thread points at a hard-deleted root) is not a root either.
            Assert.False(await db.DiscussionThreads.AnyAsync(t => t.LegacyId == 90008));
            Assert.False(await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90008));
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(11, stats2.Read);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("forum_message_hidden_status"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("forum_message_moved_notice"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("forum_post_thread_root_missing"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("attachment_post_not_migrated"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(2, await db.DiscussionThreads.CountAsync(t => t.LegacyId == 90001 || t.LegacyId == 90010));
            Assert.Equal(7, await db.DiscussionPosts.CountAsync(p => p.LegacyId >= 90000 && p.LegacyId < 91000));
            Assert.Equal(1, await db.MediaItems.CountAsync(m => m.StorageKey.StartsWith("forum_attachments/")));
            Assert.Equal(1, await db.DiscussionPostMedia.CountAsync());

            var threadA = await db.DiscussionThreads.SingleAsync(t => t.LegacyId == 90001);
            Assert.Equal(5, threadA.PostCount);

            var stored = await fixture.ObjectStore.GetAsync("forum_attachments/9001_attach.jpg");
            Assert.NotNull(stored);
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer);
            Assert.Equal(AttachmentBytes, buffer.ToArray());
        }
    }

    // A target migrated under the naive root rule holds a discussion_thread row per parent_id-0
    // message (legacy_id 90007 and 90008 here) and a discussion_post row for the moved notice
    // (90007), attributed to thread A because its own thread column resolves there, with an
    // attachment media item of its own. This seeds that state directly, then asserts that running
    // the fixed step against it converges: the stale thread rows, the stale post, its
    // discussion_post_media row and its now-unreferenced media_item all disappear, while the real
    // threads, posts and thread A's own attachment survive the same cleanup pass.
    [Fact]
    public async Task ReRunRemovesStaleThreadsAndPosts()
    {
        await RunPrerequisiteStepsAsync();
        await RunStepAsync();

        long staleMediaItemId = 0;

        await fixture.RunExclusiveAsync(async () =>
        {
            await using var db = fixture.CreateTargetContext();

            foreach (var legacyId in new[] { 90007, 90008 })
            {
                if (await db.DiscussionThreads.AnyAsync(t => t.LegacyId == legacyId))
                {
                    continue;
                }

                db.DiscussionThreads.Add(new DiscussionThread
                {
                    LegacyId = legacyId,
                    Category = DiscussionCategory.Cichlids,
                    Title = $"Thread {legacyId}",
                    State = DiscussionThreadState.Archived,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
                    PostCount = 0,
                    LastPostAt = null,
                });
            }

            await db.SaveChangesAsync();

            if (!await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90007))
            {
                var threadA = await db.DiscussionThreads.SingleAsync(t => t.LegacyId == 90001);

                var stalePost = new DiscussionPost
                {
                    ThreadId = threadA.Id,
                    LegacyId = 90007,
                    Body = "This message has been moved.",
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000400),
                    Sort = 99,
                };
                db.DiscussionPosts.Add(stalePost);
                await db.SaveChangesAsync();

                var staleMediaItem = new MediaItem
                {
                    Kind = MediaKind.Photo,
                    StorageKey = "forum_attachments/9099_stale.jpg",
                    ByteSize = 12,
                    ChecksumSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.MediaItems.Add(staleMediaItem);
                await db.SaveChangesAsync();
                staleMediaItemId = staleMediaItem.Id;

                db.DiscussionPostMedia.Add(new DiscussionPostMedia { DiscussionPostId = stalePost.Id, MediaItemId = staleMediaItem.Id, Sort = 0 });
                await db.SaveChangesAsync();
            }
        });

        await RunStepAsync();

        await using var readback = fixture.CreateTargetContext();
        Assert.False(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90007));
        Assert.False(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90008));
        Assert.False(await readback.DiscussionPosts.AnyAsync(p => p.LegacyId == 90007));
        Assert.False(await readback.DiscussionPostMedia.AnyAsync(m => m.MediaItemId == staleMediaItemId));
        Assert.False(await readback.MediaItems.AnyAsync(m => m.Id == staleMediaItemId));

        // Real roots, posts and thread A's own attachment survive the same cleanup pass.
        Assert.True(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90001));
        Assert.True(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90010));
        Assert.True(await readback.MediaItems.AnyAsync(m => m.StorageKey == "forum_attachments/9001_attach.jpg"));
    }

    // Alice (901) is matched by e-mail: seeded directly, the same way the other steps' tests seed
    // their own prerequisite profiles.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.Username == "forum-fixture-alice-901"))
        {
            var alice = new Profile
            {
                Username = "forum-fixture-alice-901",
                DisplayName = "Alice Forum Fixture",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Profiles.Add(alice);
            await db.SaveChangesAsync();

            db.ProfileIdentities.Add(new ProfileIdentity
            {
                ProfileId = alice.Id,
                Provider = "email",
                Subject = "alice.forum@example.com",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None, fixture.ObjectStore);
        await EtlRunner.RunAsync(new ForumMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("forum");
    });
}
