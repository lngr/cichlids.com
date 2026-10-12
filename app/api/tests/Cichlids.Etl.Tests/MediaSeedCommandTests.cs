using System.Security.Cryptography;
using System.Text.Json;
using Cichlids.Etl.Media;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(MediaCollection.Name)]
public sealed class MediaSeedCommandTests(MediaEtlFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task DerivesVariantsPerWidthAtTheNoUpscaleBoundaryAndUploadsNothingOnASecondRun()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int narrowUid = 71001;
            const int midUid = 71002;
            const int wideUid = 71003;

            var narrowPath = MediaTestSupport.WritePhoto(root, narrowUid, "narrow.jpg", 150, 200);
            var midPath = MediaTestSupport.WritePhoto(root, midUid, "mid.jpg", 900, 600);
            var widePath = MediaTestSupport.WritePhoto(root, wideUid, "wide.jpg", 2400, 1600);

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, narrowUid, narrowPath);
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, midUid, midPath);
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, wideUid, widePath);
            }

            var narrowMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, narrowUid, $"originals/{narrowPath}");
            var midMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, midUid, $"originals/{midPath}");
            var wideMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, wideUid, $"originals/{widePath}");

            await MediaTestSupport.RunSeedAsync(fixture, root);

            await using (var db = fixture.CreateTargetContext())
            {
                var narrowVariants = await db.MediaVariants.Where(v => v.MediaItemId == narrowMediaId).ToListAsync();
                var narrowVariant = Assert.Single(narrowVariants);
                Assert.Equal("thumb", narrowVariant.Label);
                Assert.Equal(150, narrowVariant.Width);

                // Round-trip at least one variant through the store and re-measure it, so the
                // assertion covers the actual uploaded bytes, not just the recorded row.
                var narrowVariantBytes = await MediaTestSupport.DownloadAsync(fixture, narrowVariant.StorageKey);
                Assert.Equal(150, MediaTestSupport.MeasureVariant(narrowVariantBytes).Width);

                var midVariants = await db.MediaVariants.Where(v => v.MediaItemId == midMediaId).ToListAsync();
                Assert.Equal(
                    new[] { "thumb", "small", "medium" },
                    midVariants.OrderBy(v => v.Width).Select(v => v.Label));
                Assert.DoesNotContain(midVariants, v => v.Label == "large");

                var wideVariants = await db.MediaVariants.Where(v => v.MediaItemId == wideMediaId).ToListAsync();
                Assert.Equal(
                    new[] { "thumb", "small", "medium", "large" },
                    wideVariants.OrderBy(v => v.Width).Select(v => v.Label));

                var midItem = await db.MediaItems.SingleAsync(m => m.Id == midMediaId);
                Assert.Equal(900, midItem.Width);
                Assert.Equal(600, midItem.Height);
            }

            // A second run finds every original and variant in the store and uploads nothing.
            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var second = await MediaTestSupport.RunSeedAsync(fixture, root, objectStore: recording);
            Assert.Empty(recording.PutKeys);
            Assert.Equal(0, second.OriginalsUploaded);

            await using (var db = fixture.CreateTargetContext())
            {
                Assert.Equal(1, await db.MediaVariants.CountAsync(v => v.MediaItemId == narrowMediaId));
                Assert.Equal(3, await db.MediaVariants.CountAsync(v => v.MediaItemId == midMediaId));
                Assert.Equal(4, await db.MediaVariants.CountAsync(v => v.MediaItemId == wideMediaId));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsASummaryWithTheExactCountsOfTheRun()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int photoUid = 71040;
            const int missingUid = 71041;
            const int brokenUid = 71042;

            var photoPath = MediaTestSupport.WritePhoto(root, photoUid, "counted.jpg", 900, 600);
            var brokenPath = MediaTestSupport.WriteCorruptFile(root, brokenUid, "counted-broken.jpg");

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, photoUid, photoPath);
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, missingUid, "user_pics/71041/absent.jpg");
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, brokenUid, brokenPath);
            }

            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, photoUid, $"originals/{photoPath}", targetConnectionString: target);
            await MediaTestSupport.InsertMediaItemAsync(fixture, missingUid, "originals/user_pics/71041/absent.jpg", targetConnectionString: target);
            await MediaTestSupport.InsertMediaItemAsync(fixture, brokenUid, $"originals/{brokenPath}", targetConnectionString: target);

            var summary = await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            Assert.Equal(3, summary.Processed);
            Assert.Equal(2, summary.OriginalsUploaded);
            Assert.Equal(3, summary.VariantsUploaded);
            Assert.Equal(2, summary.MetadataRecorded);
            Assert.Equal(1, summary.Missing);
            Assert.Equal(1, summary.Broken);
            Assert.True(summary.BytesTransferred > 0);
            Assert.True(summary.Elapsed > TimeSpan.Zero);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AppliesExifOrientationBeforeMeasuringAndStripsItFromGeneratedVariants()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 71010;

            // Orientation 6 rotates 90 degrees: a 900x600 raw pixel grid is logically 600x900
            // once auto-orientation applies, which is the width/height the pipeline records.
            var path = MediaTestSupport.WriteOrientedPhoto(root, uid, "rotated.jpg", 900, 600);

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
            }

            var mediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{path}");

            await MediaTestSupport.RunSeedAsync(fixture, root);

            await using var db = fixture.CreateTargetContext();
            var item = await db.MediaItems.SingleAsync(m => m.Id == mediaId);
            Assert.Equal(600, item.Width);
            Assert.Equal(900, item.Height);

            var thumb = await db.MediaVariants.SingleAsync(v => v.MediaItemId == mediaId && v.Label == "thumb");
            var thumbBytes = await MediaTestSupport.DownloadAsync(fixture, thumb.StorageKey);
            var remeasured = MediaTestSupport.MeasureVariant(thumbBytes);

            // A residual orientation tag on the generated variant would rotate it a second time
            // when re-read, flipping it back to a landscape aspect ratio. Staying portrait
            // (taller than wide) confirms the tag was baked in once and then stripped.
            Assert.True(remeasured.Height > remeasured.Width);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CountsAMissingSourceAndReportsItWithoutAffectingOtherItemsInTheRun()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int presentUid = 71020;
            const int missingUid = 71021;

            var presentPath = MediaTestSupport.WritePhoto(root, presentUid, "present.jpg", 500, 400);
            const string missingPath = "user_pics/71021/absent.jpg";

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, presentUid, presentPath);
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, missingUid, missingPath);
            }

            var presentMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, presentUid, $"originals/{presentPath}");
            var missingStorageKey = $"originals/{missingPath}";
            var missingMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, missingUid, missingStorageKey);

            var reportPath = Path.Combine(root, "missing-report.json");
            await MediaTestSupport.RunSeedAsync(fixture, root, missingOutPath: reportPath);

            await using (var db = fixture.CreateTargetContext())
            {
                var presentItem = await db.MediaItems.SingleAsync(m => m.Id == presentMediaId);
                Assert.NotNull(presentItem.ByteSize);
                Assert.Equal(500, presentItem.Width);
                Assert.Equal(400, presentItem.Height);
                Assert.Equal(
                    MediaVariantSpec.Resolve(500, 400).Count,
                    await db.MediaVariants.CountAsync(v => v.MediaItemId == presentMediaId));

                var missingItem = await db.MediaItems.SingleAsync(m => m.Id == missingMediaId);
                Assert.Null(missingItem.ByteSize);
                Assert.Null(missingItem.Width);
                Assert.Null(missingItem.Height);
            }

            var report = JsonSerializer.Deserialize<MissingReportDto>(await File.ReadAllTextAsync(reportPath), JsonOptions);
            Assert.NotNull(report);
            Assert.True(report!.MissingCount >= 1);
            Assert.Contains(report.Missing, entry => entry.StorageKey == missingStorageKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CountsABrokenImageButStillRecordsItsUploadedOriginalBytes()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int presentUid = 71030;
            const int brokenUid = 71031;

            var presentPath = MediaTestSupport.WritePhoto(root, presentUid, "present.jpg", 500, 400);
            var brokenPath = MediaTestSupport.WriteCorruptFile(root, brokenUid, "broken.jpg");

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, presentUid, presentPath);
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, brokenUid, brokenPath);
            }

            var presentMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, presentUid, $"originals/{presentPath}");
            var brokenStorageKey = $"originals/{brokenPath}";
            var brokenMediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, brokenUid, brokenStorageKey);

            var reportPath = Path.Combine(root, "broken-report.json");
            await MediaTestSupport.RunSeedAsync(fixture, root, missingOutPath: reportPath);

            await using (var db = fixture.CreateTargetContext())
            {
                var presentItem = await db.MediaItems.SingleAsync(m => m.Id == presentMediaId);
                Assert.NotNull(presentItem.ByteSize);
                Assert.Equal(500, presentItem.Width);

                var brokenItem = await db.MediaItems.SingleAsync(m => m.Id == brokenMediaId);
                var rawBytes = await File.ReadAllBytesAsync(Path.Combine(root, brokenPath));
                Assert.Equal(rawBytes.LongLength, brokenItem.ByteSize);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(rawBytes)), brokenItem.ChecksumSha256);
                Assert.Null(brokenItem.Width);
                Assert.Null(brokenItem.Height);
                Assert.Empty(await db.MediaVariants.Where(v => v.MediaItemId == brokenMediaId).ToListAsync());
            }

            var report = JsonSerializer.Deserialize<MissingReportDto>(await File.ReadAllTextAsync(reportPath), JsonOptions);
            Assert.NotNull(report);
            Assert.True(report!.BrokenCount >= 1);
            Assert.Contains(report.Broken, key => key == brokenStorageKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // === decisions from the object store state ======================================================

    private sealed record SeededPhoto(string LegacyPath, string StorageKey, string Sha256);

    private async Task<SeededPhoto> AddPhotoAsync(string root, string target, int uid, int width, int height = 400)
    {
        var path = MediaTestSupport.WritePhoto(root, uid, $"p{uid}.jpg", width, height);
        await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
        {
            await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
        }

        var key = $"originals/{path}";
        await MediaTestSupport.InsertMediaItemAsync(fixture, uid, key, targetConnectionString: target);
        return new SeededPhoto(path, key, MediaTestSupport.Sha256OfFile(root, path));
    }

    private async Task ResetDatabaseAsync(string newTarget, params (int Uid, string Key)[] items)
    {
        foreach (var (uid, key) in items)
        {
            await MediaTestSupport.InsertMediaItemAsync(fixture, uid, key, targetConnectionString: newTarget);
        }
    }

    [Fact]
    public async Task UploadsNothingAfterTheDatabaseWasResetAndRecordsTheSameMetadata()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var firstTarget = await fixture.CreateIsolatedTargetAsync();
            var wide = await AddPhotoAsync(root, firstTarget, 71100, 2400, 1600);
            var narrow = await AddPhotoAsync(root, firstTarget, 71101, 150, 200);

            var first = await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: firstTarget);
            Assert.Equal(2, first.OriginalsUploaded);
            Assert.Equal(5, first.VariantsUploaded);

            var secondTarget = await fixture.CreateIsolatedTargetAsync();
            await ResetDatabaseAsync(secondTarget, (71100, wide.StorageKey), (71101, narrow.StorageKey));

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var second = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: secondTarget, objectStore: recording);

            Assert.Empty(recording.PutKeys);
            Assert.Empty(recording.GetKeys);
            Assert.Equal(0, second.OriginalsUploaded);
            Assert.Equal(2, second.OriginalsPresent);
            Assert.Equal(0, second.VariantsUploaded);
            Assert.Equal(5, second.VariantsPresent);
            Assert.Equal(2, second.MetadataRecorded);
            Assert.Equal(0, second.BytesTransferred);

            await using var before = fixture.CreateTargetContext(firstTarget);
            await using var after = fixture.CreateTargetContext(secondTarget);
            var itemsBefore = await before.MediaItems.OrderBy(m => m.StorageKey).ToListAsync();
            var itemsAfter = await after.MediaItems.OrderBy(m => m.StorageKey).ToListAsync();
            Assert.Equal(
                itemsBefore.Select(m => (m.StorageKey, m.ByteSize, m.ChecksumSha256, m.Width, m.Height)),
                itemsAfter.Select(m => (m.StorageKey, m.ByteSize, m.ChecksumSha256, m.Width, m.Height)));
            Assert.All(itemsAfter, m => Assert.NotNull(m.ByteSize));

            var variantsBefore = await before.MediaVariants.Select(v => new { v.StorageKey, v.Label, v.Width, v.ByteSize }).OrderBy(v => v.StorageKey).ToListAsync();
            var variantsAfter = await after.MediaVariants.Select(v => new { v.StorageKey, v.Label, v.Width, v.ByteSize }).OrderBy(v => v.StorageKey).ToListAsync();
            Assert.Equal(5, variantsAfter.Count);
            Assert.Equal(variantsBefore, variantsAfter);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RestoresADeletedOriginalWithTheChecksumOfTheLegacyFile()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var photo = await AddPhotoAsync(root, target, 71110, 900, 600);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            await fixture.ObjectStore.DeleteAsync(photo.StorageKey);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(1, summary.OriginalsUploaded);
            Assert.Equal(0, summary.VariantsUploaded);
            Assert.Equal([photo.StorageKey], recording.PutKeys);

            var head = await fixture.ObjectStore.HeadAsync(photo.StorageKey);
            Assert.NotNull(head);
            Assert.Equal(photo.Sha256, head!.Metadata[MediaObjectMetadata.SourceSha256Key]);
            Assert.Equal(photo.Sha256, head.Metadata[MediaObjectMetadata.Sha256Key]);
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(root, photo.LegacyPath)),
                await MediaTestSupport.DownloadAsync(fixture, photo.StorageKey));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadsAnOverwrittenOriginalAndAnOriginalWithoutMetadataAgain()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var overwritten = await AddPhotoAsync(root, target, 71120, 500);
            var bare = await AddPhotoAsync(root, target, 71121, 500);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            await fixture.ObjectStore.PutAsync(
                overwritten.StorageKey,
                new MemoryStream([1, 2, 3]),
                "image/jpeg",
                new Dictionary<string, string>
                {
                    [MediaObjectMetadata.SourceSha256Key] = "0000",
                    [MediaObjectMetadata.PipelineKey] = MediaObjectMetadata.OriginalPipelineVersion.ToString(),
                });
            await fixture.ObjectStore.PutAsync(bare.StorageKey, new MemoryStream([4, 5, 6]), "image/jpeg");

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(2, summary.OriginalsUploaded);
            Assert.Equal(0, summary.VariantsUploaded);
            foreach (var photo in new[] { overwritten, bare })
            {
                Assert.Equal(
                    await File.ReadAllBytesAsync(Path.Combine(root, photo.LegacyPath)),
                    await MediaTestSupport.DownloadAsync(fixture, photo.StorageKey));
                var head = await fixture.ObjectStore.HeadAsync(photo.StorageKey);
                Assert.Equal(photo.Sha256, head!.Metadata[MediaObjectMetadata.SourceSha256Key]);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RegeneratesADeletedVariantAndAVariantWithAnOlderPipeline()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var photo = await AddPhotoAsync(root, target, 71130, 2400, 1600);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            var deletedKey = MediaStorageKeys.BuildVariantKey(photo.StorageKey, "small");
            var outdatedKey = MediaStorageKeys.BuildVariantKey(photo.StorageKey, "medium");
            await fixture.ObjectStore.DeleteAsync(deletedKey);
            await fixture.ObjectStore.PutAsync(
                outdatedKey,
                new MemoryStream([9, 9, 9]),
                "image/jpeg",
                new Dictionary<string, string>
                {
                    [MediaObjectMetadata.SourceSha256Key] = photo.Sha256,
                    [MediaObjectMetadata.PipelineKey] = (MediaObjectMetadata.VariantPipelineVersion - 1).ToString(),
                });

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(0, summary.OriginalsUploaded);
            Assert.Equal(2, summary.VariantsUploaded);
            Assert.Equal(2, summary.VariantsPresent);
            Assert.Equivalent(new[] { deletedKey, outdatedKey }, recording.PutKeys);

            var regenerated = await fixture.ObjectStore.HeadAsync(outdatedKey);
            Assert.Equal(
                MediaObjectMetadata.VariantPipelineVersion.ToString(),
                regenerated!.Metadata[MediaObjectMetadata.PipelineKey]);
            Assert.Equal(800, MediaTestSupport.MeasureVariant(await MediaTestSupport.DownloadAsync(fixture, outdatedKey)).Width);

            await using var db = fixture.CreateTargetContext(target);
            var medium = await db.MediaVariants.SingleAsync(v => v.StorageKey == outdatedKey);
            Assert.Equal(regenerated.Size, medium.ByteSize);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordsAPresentBrokenOriginalFromItsMetadataWithoutUploading()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 71140;
            var path = MediaTestSupport.WriteCorruptFile(root, uid, "broken.jpg");
            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
            }

            var firstTarget = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{path}", targetConnectionString: firstTarget);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: firstTarget);

            var secondTarget = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{path}", targetConnectionString: secondTarget);
            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: secondTarget, objectStore: recording);

            Assert.Empty(recording.PutKeys);
            Assert.Equal(1, summary.Broken);
            Assert.Equal(1, summary.OriginalsPresent);

            await using var db = fixture.CreateTargetContext(secondTarget);
            var item = await db.MediaItems.SingleAsync();
            Assert.Equal(MediaTestSupport.Sha256OfFile(root, path), item.ChecksumSha256);
            Assert.NotNull(item.ByteSize);
            Assert.Null(item.Width);
            Assert.Null(item.Height);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeedsAForumAttachmentFromTheStoreAndDownloadsItOnlyOnce()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var forumPhotoPath = MediaTestSupport.WritePhoto(root, 71150, "forum.jpg", 900, 600);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, forumPhotoPath));
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            const string key = "forum_attachments/71150_forum.jpg";
            await fixture.ObjectStore.PutAsync(
                key,
                new MemoryStream(bytes),
                "image/jpeg",
                MediaObjectMetadata.ForOriginal(sha, sha, photo: null));

            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71150, key, targetConnectionString: target);

            var firstRun = new RecordingObjectStore(fixture.ObjectStore);
            var first = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: firstRun);
            Assert.Equal(3, first.VariantsUploaded);
            Assert.Equal(0, first.Missing);
            Assert.Equal([key], firstRun.GetKeys);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var second = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);
            Assert.Empty(recording.GetKeys);
            Assert.Empty(recording.PutKeys);
            Assert.Equal(3, second.VariantsPresent);

            await using var db = fixture.CreateTargetContext(target);
            var item = await db.MediaItems.SingleAsync();
            Assert.Equal(bytes.LongLength, item.ByteSize);
            Assert.Equal(sha, item.ChecksumSha256);
            Assert.Equal(900, item.Width);
            Assert.Equal(600, item.Height);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompletesTheMetadataOfASanitizedForumAttachmentAndKeepsItsLegacySourceChecksum()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var legacyBytes = PhotoFixtures.PhoneJpeg(900, 600);
            var legacySha = Convert.ToHexStringLower(SHA256.HashData(legacyBytes));
            var sanitized = MediaMetadataSanitizer.Sanitize(legacyBytes);
            var sanitizedSha = Convert.ToHexStringLower(SHA256.HashData(sanitized));
            const string key = "forum_attachments/71151_phone.jpg";
            await fixture.ObjectStore.PutAsync(
                key, new MemoryStream(sanitized), "image/jpeg", MediaObjectMetadata.ForOriginal(sanitizedSha, legacySha, photo: null));

            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71151, key, targetConnectionString: target);

            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            var head = await fixture.ObjectStore.HeadAsync(key);
            Assert.Equal(legacySha, head!.Metadata[MediaObjectMetadata.SourceSha256Key]);
            Assert.Equal(sanitizedSha, head.Metadata[MediaObjectMetadata.Sha256Key]);
            Assert.Equal("900", head.Metadata[MediaObjectMetadata.WidthKey]);
            Assert.Equal(sanitized, await MediaTestSupport.DownloadAsync(fixture, key));

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target, objectStore: recording);
            Assert.Empty(recording.PutKeys);

            await using var db = fixture.CreateTargetContext(target);
            var item = await db.MediaItems.SingleAsync();
            Assert.Equal(sanitized.LongLength, item.ByteSize);
            Assert.Equal(sanitizedSha, item.ChecksumSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SanitizesAForumAttachmentStoredWithItsLocationDataByAnEarlierPipeline()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var legacyBytes = PhotoFixtures.PhoneJpeg(900, 600);
            var legacySha = Convert.ToHexStringLower(SHA256.HashData(legacyBytes));
            const string key = "forum_attachments/71152_phone.jpg";
            await fixture.ObjectStore.PutAsync(
                key,
                new MemoryStream(legacyBytes),
                "image/jpeg",
                new Dictionary<string, string>
                {
                    [MediaObjectMetadata.Sha256Key] = legacySha,
                    [MediaObjectMetadata.SourceSha256Key] = legacySha,
                    [MediaObjectMetadata.PipelineKey] = "1",
                });

            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71152, key, targetConnectionString: target);

            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            var stored = await MediaTestSupport.DownloadAsync(fixture, key);
            Assert.False(PhotoFixtures.ContainsSecret(stored), "A SECRET- value survived in the stored attachment.");
            var head = await fixture.ObjectStore.HeadAsync(key);
            Assert.Equal(legacySha, head!.Metadata[MediaObjectMetadata.SourceSha256Key]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(stored)), head.Metadata[MediaObjectMetadata.Sha256Key]);
            Assert.Equal(MediaObjectMetadata.OriginalPipelineVersion.ToString(), head.Metadata[MediaObjectMetadata.PipelineKey]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsAForumAttachmentMissingFromTheStore()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(
                fixture, 71160, "forum_attachments/71160_gone.jpg", targetConnectionString: target);

            var summary = await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            Assert.Equal(1, summary.Missing);
            Assert.Equal(0, summary.MetadataRecorded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordsTheChecksumOfTheStoredBytesAndReuploadsAnOriginalWithoutIt()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var distinct = await AddPhotoAsync(root, target, 71170, 500, 400);
            var withoutChecksum = await AddPhotoAsync(root, target, 71171, 500, 400);
            await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);

            IReadOnlyDictionary<string, string> Metadata(SeededPhoto photo, bool withSha) =>
                new Dictionary<string, string>(MediaObjectMetadata.ForOriginal("feedbeef", photo.Sha256, new PhotoInfo(500, 400)))
                    .Where(kv => withSha || kv.Key != MediaObjectMetadata.Sha256Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

            foreach (var (photo, withSha) in new[] { (distinct, true), (withoutChecksum, false) })
            {
                await fixture.ObjectStore.PutAsync(
                    photo.StorageKey,
                    new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(root, photo.LegacyPath))),
                    "image/jpeg",
                    Metadata(photo, withSha));
            }

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(1, summary.OriginalsUploaded);
            Assert.Equal([withoutChecksum.StorageKey], recording.PutKeys.Where(k => k.StartsWith("originals/")));

            await using var db = fixture.CreateTargetContext(target);
            Assert.Equal("feedbeef", (await db.MediaItems.SingleAsync(m => m.StorageKey == distinct.StorageKey)).ChecksumSha256);
            Assert.Equal(
                withoutChecksum.Sha256,
                (await db.MediaItems.SingleAsync(m => m.StorageKey == withoutChecksum.StorageKey)).ChecksumSha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordsATruncatedImageAsBrokenAndDoesNotUploadItAgain()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 71180;
            var path = MediaTestSupport.WritePhoto(root, uid, "damaged.tif", 2400, 1600);
            var fullPath = Path.Combine(root, path);
            var all = await File.ReadAllBytesAsync(fullPath);

            // Cutting the file inside its pixel data makes the image undecodable.
            all = all[..(all.Length * 6 / 10)];

            await File.WriteAllBytesAsync(fullPath, all);
            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
            }

            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{path}", targetConnectionString: target);

            var first = await MediaTestSupport.RunSeedAsync(fixture, root, targetConnectionString: target);
            Assert.Equal(1, first.Broken);

            await using (var db = fixture.CreateTargetContext(target))
            {
                var item = await db.MediaItems.SingleAsync();
                Assert.Null(item.Width);
                Assert.Null(item.Height);
                Assert.Empty(await db.MediaVariants.ToListAsync());
            }

            var head = await fixture.ObjectStore.HeadAsync($"originals/{path}");
            Assert.Equal("false", head!.Metadata[MediaObjectMetadata.DecodableKey]);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var second = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);
            Assert.Empty(recording.PutKeys);
            Assert.Equal(1, second.Broken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CountsAForumAttachmentThatVanishesBetweenHeadAndDownloadAsMissing()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const string key = "forum_attachments/71190_vanishing.jpg";
            await fixture.ObjectStore.PutAsync(key, new MemoryStream([1, 2, 3]), "image/jpeg");
            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71190, key, targetConnectionString: target);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            recording.VanishOnGet.Add(key);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(1, summary.Processed);
            Assert.Equal(1, summary.Missing);
            Assert.Equal(0, summary.MetadataRecorded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsAForumAttachmentAboveTheDownloadCapAndSeedsTheOtherItems()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const string oversizedKey = "forum_attachments/71191_oversized.jpg";
            const string regularKey = "forum_attachments/71192_regular.jpg";
            await fixture.ObjectStore.PutAsync(oversizedKey, new MemoryStream([1, 2, 3]), "image/jpeg");
            await fixture.ObjectStore.PutAsync(regularKey, new MemoryStream([4, 5, 6]), "image/jpeg");
            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71191, oversizedKey, targetConnectionString: target);
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71192, regularKey, targetConnectionString: target);
            var reportPath = Path.Combine(root, "report.json");

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            recording.TooLargeOnGet.Add(oversizedKey);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, missingOutPath: reportPath, targetConnectionString: target, objectStore: recording);

            Assert.Equal(2, summary.Processed);
            Assert.Equal(1, summary.Missing);
            Assert.Equal(1, summary.MetadataRecorded);
            var report = JsonSerializer.Deserialize<MissingReportDto>(await File.ReadAllTextAsync(reportPath), JsonOptions)!;
            var entry = Assert.Single(report.Missing);
            Assert.Equal(oversizedKey, entry.StorageKey);
            Assert.Contains("download limit", entry.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CountsAForumAttachmentThatVanishesBeforeItsMissingVariantsAreGeneratedAsMissing()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var photoPath = MediaTestSupport.WritePhoto(root, 71193, "forum.jpg", 900, 600);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(root, photoPath));
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            const string vanishingKey = "forum_attachments/71193_vanishing.jpg";
            await fixture.ObjectStore.PutAsync(
                vanishingKey, new MemoryStream(bytes), "image/jpeg", MediaObjectMetadata.ForOriginal(sha, sha, new PhotoInfo(900, 600)));
            const string regularKey = "forum_attachments/71194_regular.jpg";
            await fixture.ObjectStore.PutAsync(
                regularKey, new MemoryStream(bytes), "image/jpeg", MediaObjectMetadata.ForOriginal(sha, sha, new PhotoInfo(900, 600)));
            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71193, vanishingKey, targetConnectionString: target);
            await MediaTestSupport.InsertMediaItemAsync(fixture, 71194, regularKey, targetConnectionString: target);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            recording.VanishOnGet.Add(vanishingKey);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(2, summary.Processed);
            Assert.Equal(1, summary.Missing);
            Assert.Equal(1, summary.MetadataRecorded);
            Assert.Equal(3, summary.VariantsUploaded);
            Assert.DoesNotContain(recording.PutKeys, k => k.Contains(vanishingKey, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadsNoVariantWhenAStoredDecodableOriginalTurnsOutNotToDecode()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 71195;
            var path = MediaTestSupport.WritePhoto(root, uid, "late-broken.tif", 2400, 1600);
            var fullPath = Path.Combine(root, path);
            var all = await File.ReadAllBytesAsync(fullPath);
            all = all[..(all.Length * 6 / 10)];
            await File.WriteAllBytesAsync(fullPath, all);
            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
            }

            var key = $"originals/{path}";
            var sha = Convert.ToHexStringLower(SHA256.HashData(all));
            await fixture.ObjectStore.PutAsync(
                key, new MemoryStream(all), "image/tiff", MediaObjectMetadata.ForOriginal(sha, sha, new PhotoInfo(2400, 1600)));
            var target = await fixture.CreateIsolatedTargetAsync();
            await MediaTestSupport.InsertMediaItemAsync(fixture, uid, key, targetConnectionString: target);

            var recording = new RecordingObjectStore(fixture.ObjectStore);
            var summary = await MediaTestSupport.RunSeedAsync(
                fixture, root, targetConnectionString: target, objectStore: recording);

            Assert.Equal(1, summary.Broken);
            Assert.Equal(0, summary.VariantsUploaded);
            Assert.Equal([key], recording.PutKeys);
            var head = await fixture.ObjectStore.HeadAsync(key);
            Assert.Equal("false", head!.Metadata[MediaObjectMetadata.DecodableKey]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Mirrors the private MissingReportDto/MissingEntryDto shape MediaSeedCommand writes: a
    // separate, test-local copy since the production records are private to that class and the
    // JSON contract, not the CLR type, is what a consumer of --missing-out actually depends on.
    private sealed record MissingEntryDto(long MediaItemId, string StorageKey, string Reason);

    private sealed record MissingReportDto(
        DateTimeOffset GeneratedAt, int MissingCount, int BrokenCount, List<MissingEntryDto> Missing, List<string> Broken);
}
