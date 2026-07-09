using System.Security.Cryptography;
using System.Text.Json;
using Cichlids.Etl.Media;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(MediaCollection.Name)]
public sealed class MediaSeedCommandTests(MediaEtlFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task DerivesVariantsPerWidthAtTheNoUpscaleBoundaryAndSkipsThemOnASecondRun()
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

            var exitCode = await MediaTestSupport.RunSeedAsync(fixture, root);
            Assert.Equal(0, exitCode);

            await using (var db = fixture.CreateTargetContext())
            {
                var narrowVariants = await db.MediaVariants.Where(v => v.MediaItemId == narrowMediaId).ToListAsync();
                var narrowVariant = Assert.Single(narrowVariants);
                Assert.Equal("thumb", narrowVariant.Label);
                Assert.Equal(150, narrowVariant.Width);

                // Round-trip at least one variant through the store and re-measure it, so the
                // assertion covers the actual uploaded bytes, not just the recorded row.
                var narrowVariantBytes = await MediaTestSupport.DownloadAsync(fixture, narrowVariant.StorageKey);
                Assert.Equal(150, ImageProcessor.Measure(narrowVariantBytes).Width);

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

            // A second run with the default onlyMissing: true finds every item already
            // complete, so it skips them outright and leaves the recorded variants untouched.
            var secondExitCode = await MediaTestSupport.RunSeedAsync(fixture, root);
            Assert.Equal(0, secondExitCode);

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

            var exitCode = await MediaTestSupport.RunSeedAsync(fixture, root);
            Assert.Equal(0, exitCode);

            await using var db = fixture.CreateTargetContext();
            var item = await db.MediaItems.SingleAsync(m => m.Id == mediaId);
            Assert.Equal(600, item.Width);
            Assert.Equal(900, item.Height);

            var thumb = await db.MediaVariants.SingleAsync(v => v.MediaItemId == mediaId && v.Label == "thumb");
            var thumbBytes = await MediaTestSupport.DownloadAsync(fixture, thumb.StorageKey);
            var remeasured = ImageProcessor.Measure(thumbBytes);

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
            var exitCode = await MediaTestSupport.RunSeedAsync(fixture, root, missingOutPath: reportPath);
            Assert.Equal(0, exitCode);

            await using (var db = fixture.CreateTargetContext())
            {
                var presentItem = await db.MediaItems.SingleAsync(m => m.Id == presentMediaId);
                Assert.NotNull(presentItem.ByteSize);
                Assert.Equal(500, presentItem.Width);
                Assert.Equal(400, presentItem.Height);
                Assert.Equal(
                    MediaVariantSpec.Resolve(500).Count,
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
            var exitCode = await MediaTestSupport.RunSeedAsync(fixture, root, missingOutPath: reportPath);
            Assert.Equal(0, exitCode);

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

    // Mirrors the private MissingReportDto/MissingEntryDto shape MediaSeedCommand writes: a
    // separate, test-local copy since the production records are private to that class and the
    // JSON contract, not the CLR type, is what a consumer of --missing-out actually depends on.
    private sealed record MissingEntryDto(long MediaItemId, string StorageKey, string Reason);

    private sealed record MissingReportDto(
        DateTimeOffset GeneratedAt, int MissingCount, int BrokenCount, List<MissingEntryDto> Missing, List<string> Broken);
}
