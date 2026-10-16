using System.Security.Cryptography;
using System.Text;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;
using Cichlids.Tests.Shared;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

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

        // Read counts every row phorum_messages actually has: 12 in the fixture (90001-90008,
        // 90010, 90020, 90030, 90040). One (90004) is hidden, one (90007) is a moved notice, and
        // two (90008, 90020) have a thread column that resolves to no surviving root; the
        // remaining 8 are visible, forum-mapped posts.
        Assert.Equal(12, stats1.Read);
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

            // Thread D's root 90040 is by a guest named by an address that comment uid 5014 uses
            // as well: both get the same generated guest name.
            var addressGuestRoot = await db.DiscussionPosts.SingleAsync(p => p.LegacyId == 90040);
            Assert.Null(addressGuestRoot.AuthorProfileId);
            Assert.Equal(SeedGuestNames.Shared, addressGuestRoot.PosterName);

            // 90004 (hidden) never appears anywhere.
            Assert.DoesNotContain(postsInOrder, p => p.LegacyId == 90004);
            Assert.False(await db.DiscussionPosts.AnyAsync(p => p.LegacyId == 90004));

            // Thread B: the attachment thread.
            var threadB = await db.DiscussionThreads
                .Include(t => t.Posts).ThenInclude(p => p.MediaLinks)
                .SingleAsync(t => t.LegacyId == 90010);
            Assert.Equal(DiscussionCategory.African, threadB.Category);
            Assert.Single(threadB.Posts);

            var attachmentPost = threadB.Posts[0];
            var media = Assert.Single(attachmentPost.MediaLinks);
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

        // A row holding a raw address gets its guest name on the rerun.
        await using (var db = fixture.CreateTargetContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE discussion_post SET poster_name = 'shared.guest@example.com' WHERE legacy_id = 90040");
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(12, stats2.Read);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("forum_message_hidden_status"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("forum_message_moved_notice"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("forum_post_thread_root_missing"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("attachment_post_not_migrated"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(2, await db.DiscussionThreads.CountAsync(t => t.LegacyId == 90001 || t.LegacyId == 90010));
            Assert.Equal(8, await db.DiscussionPosts.CountAsync(p => p.LegacyId >= 90000 && p.LegacyId < 91000));
            Assert.Equal(1, await db.MediaItems.CountAsync(m => m.StorageKey.StartsWith("forum_attachments/")));
            var attachment = await db.MediaLinks.SingleAsync(l => l.DiscussionPostId != null);
            Assert.Equal(MediaLinkRole.Attachment, attachment.Role);
            Assert.Equal(0, attachment.Position);

            var threadA = await db.DiscussionThreads.SingleAsync(t => t.LegacyId == 90001);
            Assert.Equal(5, threadA.PostCount);

            Assert.Equal(SeedGuestNames.Shared, (await db.DiscussionPosts.SingleAsync(p => p.LegacyId == 90040)).PosterName);
            Assert.Equal("GuestPoster", (await db.DiscussionPosts.SingleAsync(p => p.LegacyId == 90002)).PosterName);

            var stored = await fixture.ObjectStore.GetAsync("forum_attachments/9001_attach.jpg");
            Assert.NotNull(stored);
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer);
            Assert.Equal(AttachmentBytes, buffer.ToArray());
        }
    }

    private const string AttachmentKey = "forum_attachments/9001_attach.jpg";

    [Fact]
    public async Task UploadsNoPresentAttachmentAgainEvenWhenTheDatabaseRowsAreGone()
    {
        await RunPrerequisiteStepsAsync();
        await RunStepAsync();

        var recording = new RecordingObjectStore(fixture.ObjectStore);
        await RunStepAsync(recording);
        Assert.Empty(recording.PutKeys);

        // After a database reset the media_item rows are gone while the object stays in the store.
        await using (var db = fixture.CreateTargetContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM media_link WHERE discussion_post_id IS NOT NULL");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM media_item WHERE storage_key LIKE 'forum_attachments/%'");
        }

        var afterReset = new RecordingObjectStore(fixture.ObjectStore);
        var stats = await RunStepAsync(afterReset);

        Assert.Empty(afterReset.PutKeys);
        Assert.Contains(stats.Warnings, w => w.Contains("forum attachments: 0 uploaded"));
        await using var readback = fixture.CreateTargetContext();
        Assert.Equal(1, await readback.MediaItems.CountAsync(m => m.StorageKey == AttachmentKey));
    }

    [Fact]
    public async Task UploadsADeletedAttachmentAgainWithItsChecksumMetadata()
    {
        await RunPrerequisiteStepsAsync();
        await RunStepAsync();
        await fixture.ObjectStore.DeleteAsync(AttachmentKey);

        var recording = new RecordingObjectStore(fixture.ObjectStore);
        await RunStepAsync(recording);

        Assert.Equal([AttachmentKey], recording.PutKeys);
        var head = await fixture.ObjectStore.HeadAsync(AttachmentKey);
        Assert.NotNull(head);
        var checksum = Convert.ToHexStringLower(SHA256.HashData(AttachmentBytes));
        Assert.Equal(AttachmentBytes.LongLength, head!.Size);
        Assert.Equal(checksum, head.Metadata[MediaObjectMetadata.Sha256Key]);
        Assert.Equal(checksum, head.Metadata[MediaObjectMetadata.SourceSha256Key]);
    }

    private const string PhotoAttachmentKey = "forum_attachments/9003_phone.jpg";

    [Fact]
    public async Task StoresAJpegAttachmentWithoutLocationDataAndWithTheObjectMetadataContract()
    {
        await RunPrerequisiteStepsAsync();
        var legacyBytes = PhotoFixtures.PhoneJpeg(640, 480, orientation: 6);
        await AddLegacyAttachmentAsync(9003, "phone.jpg", legacyBytes, messageId: 90010);

        try
        {
            await RunStepAsync();

            var stored = await DownloadAsync(PhotoAttachmentKey);
            Assert.False(PhotoFixtures.ContainsSecret(stored), "A SECRET- value survived in the stored attachment.");
            var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(stored));
            Assert.Empty(directories.OfType<GpsDirectory>());
            var subIfd = Assert.Single(directories.OfType<ExifSubIfdDirectory>());
            Assert.Equal(PhotoFixtures.CaptureDate, subIfd.GetString(ExifDirectoryBase.TagDateTimeOriginal));
            Assert.Equal(6, Assert.Single(directories.OfType<ExifIfd0Directory>()).GetInt32(ExifDirectoryBase.TagOrientation));
            Assert.NotEmpty(directories.OfType<IccDirectory>());

            var storedChecksum = Convert.ToHexStringLower(SHA256.HashData(stored));
            var head = await fixture.ObjectStore.HeadAsync(PhotoAttachmentKey);
            Assert.NotNull(head);
            Assert.Equal(storedChecksum, head!.Metadata[MediaObjectMetadata.Sha256Key]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(legacyBytes)), head.Metadata[MediaObjectMetadata.SourceSha256Key]);
            Assert.Equal(MediaObjectMetadata.OriginalPipelineVersion.ToString(), head.Metadata[MediaObjectMetadata.PipelineKey]);

            await using (var db = fixture.CreateTargetContext())
            {
                var mediaItem = await db.MediaItems.SingleAsync(m => m.StorageKey == PhotoAttachmentKey);
                Assert.Equal(stored.LongLength, mediaItem.ByteSize);
                Assert.Equal(storedChecksum, mediaItem.ChecksumSha256);
            }

            var rerun = new RecordingObjectStore(fixture.ObjectStore);
            await RunStepAsync(rerun);
            Assert.DoesNotContain(PhotoAttachmentKey, rerun.PutKeys);
        }
        finally
        {
            await RemoveLegacyAttachmentAsync(9003, PhotoAttachmentKey);
        }
    }

    [Fact]
    public async Task ReplacesAnAttachmentStoredWithItsLocationDataBySanitizedBytes()
    {
        await RunPrerequisiteStepsAsync();
        var legacyBytes = PhotoFixtures.PhoneJpeg(640, 480);
        var legacyChecksum = Convert.ToHexStringLower(SHA256.HashData(legacyBytes));
        await AddLegacyAttachmentAsync(9003, "phone.jpg", legacyBytes, messageId: 90010);

        try
        {
            // An object stored by an earlier pipeline version: the legacy bytes, location data included.
            await fixture.ObjectStore.PutAsync(
                PhotoAttachmentKey,
                new MemoryStream(legacyBytes),
                "image/jpeg",
                new Dictionary<string, string>
                {
                    [MediaObjectMetadata.Sha256Key] = legacyChecksum,
                    [MediaObjectMetadata.SourceSha256Key] = legacyChecksum,
                    [MediaObjectMetadata.PipelineKey] = "1",
                });

            await RunStepAsync();

            Assert.False(PhotoFixtures.ContainsSecret(await DownloadAsync(PhotoAttachmentKey)), "A SECRET- value survived in the stored attachment.");
        }
        finally
        {
            await RemoveLegacyAttachmentAsync(9003, PhotoAttachmentKey);
        }
    }

    [Fact]
    public async Task StoresNoImageAttachmentWhoseMetadataCannotBeRemovedAndDeletesAnEarlierCopy()
    {
        await RunPrerequisiteStepsAsync();
        const string key = "forum_attachments/9004_scan.tif";
        var legacyBytes = PhotoFixtures.UndecodableTiffWithGps();
        await AddLegacyAttachmentAsync(9004, "scan.tif", legacyBytes, messageId: 90010);

        try
        {
            await fixture.ObjectStore.PutAsync(key, new MemoryStream(legacyBytes), "image/tiff");

            var stats = await RunStepAsync();

            Assert.Equal(1, stats.SkipReasons.GetValueOrDefault("attachment_unsanitizable"));
            Assert.Null(await fixture.ObjectStore.HeadAsync(key));
            await using var db = fixture.CreateTargetContext();
            Assert.False(await db.MediaItems.AnyAsync(m => m.StorageKey == key));
        }
        finally
        {
            await RemoveLegacyAttachmentAsync(9004, key);
        }
    }

    // A target migrated under the naive root rule holds a discussion_thread row per parent_id-0
    // message (legacy_id 90007 and 90008 here) and a discussion_post row for the moved notice
    // (90007), attributed to thread A because its own thread column resolves there, with an
    // attachment media item of its own. This seeds that state directly, then asserts that running
    // the fixed step against it converges: the stale thread rows, the stale post, its
    // media link and its unreferenced media_item all disappear, while the real
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

                db.MediaLinks.Add(MediaLink.ForDiscussionPost(stalePost.Id, staleMediaItem.Id));
                await db.SaveChangesAsync();
            }
        });

        await RunStepAsync();

        await using var readback = fixture.CreateTargetContext();
        Assert.False(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90007));
        Assert.False(await readback.DiscussionThreads.AnyAsync(t => t.LegacyId == 90008));
        Assert.False(await readback.DiscussionPosts.AnyAsync(p => p.LegacyId == 90007));
        Assert.False(await readback.MediaLinks.AnyAsync(l => l.MediaItemId == staleMediaItemId));
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

    private async Task AddLegacyAttachmentAsync(int fileId, string filename, byte[] bytes, int messageId)
    {
        await using var connection = new MySqlConnection(fixture.LegacyConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            $"""
            INSERT INTO {ForumMigrationStep.PhorumDatabase}.phorum_files (file_id, filename, file_data, message_id, link)
            VALUES (@fileId, @filename, @data, @messageId, 'message')
            """,
            connection);
        command.Parameters.AddWithValue("fileId", fileId);
        command.Parameters.AddWithValue("filename", filename);
        command.Parameters.AddWithValue("data", Convert.ToBase64String(bytes));
        command.Parameters.AddWithValue("messageId", messageId);
        await command.ExecuteNonQueryAsync();
    }

    // Removes a test-only attachment from the legacy rows, the target rows and the object store,
    // so the other tests of the collection see the shared seed only.
    private async Task RemoveLegacyAttachmentAsync(int fileId, string storageKey)
    {
        await using (var connection = new MySqlConnection(fixture.LegacyConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                $"DELETE FROM {ForumMigrationStep.PhorumDatabase}.phorum_files WHERE file_id = @fileId", connection);
            command.Parameters.AddWithValue("fileId", fileId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = fixture.CreateTargetContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM media_link WHERE media_item_id IN (SELECT id FROM media_item WHERE storage_key = {storageKey})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM media_item WHERE storage_key = {storageKey}");
        }

        await fixture.ObjectStore.DeleteAsync(storageKey);
    }

    private async Task<byte[]> DownloadAsync(string storageKey)
    {
        await using var stream = await fixture.ObjectStore.GetAsync(storageKey);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private Task<StepStatistics> RunStepAsync(IObjectStore? objectStore = null) => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, CancellationToken.None, objectStore ?? fixture.ObjectStore);
        await EtlRunner.RunAsync(new ForumMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("forum");
    });
}
