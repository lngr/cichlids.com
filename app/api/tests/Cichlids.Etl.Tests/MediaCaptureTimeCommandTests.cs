using System.Security.Cryptography;
using Cichlids.Etl.Media;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace Cichlids.Etl.Tests;

[Collection(MediaCollection.Name)]
public sealed class MediaCaptureTimeCommandTests(MediaEtlFixture fixture)
{
    [Fact]
    public async Task GivenArchivePhotosWithAndWithoutExif_WhenTheBackfillRuns_ThenEachRowStoresItsCaptureTimeAndCountsAreReported()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var withOffset = await AddPhotoAsync(root, target, 74001, "offset.jpg", PhotoFixtures.PhoneJpeg(300, 200));
            var withoutOffset = await AddPhotoAsync(root, target, 74002, "local.jpg", PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09"));
            var withoutExif = await AddPhotoAsync(root, target, 74003, "plain.jpg", PhotoFixtures.BaseJpeg(64, 48));
            var zeroDate = await AddPhotoAsync(root, target, 74004, "zero.jpg", PhotoFixtures.JpegWithCaptureTime("0000:00:00 00:00:00"));
            var broken = await AddPhotoAsync(root, target, 74005, "broken.jpg", "not a real jpeg"u8.ToArray());
            var missing = await AddRowAsync(target, 74006, "user_pics/74006/absent.jpg");
            var hashes = HashFiles(root);

            var summary = await RunAsync(root, target);

            Assert.Equal(6, summary.Candidates);
            Assert.Equal(5, summary.Examined);
            Assert.Equal(2, summary.Found);
            Assert.Equal(1, summary.NoDate);
            Assert.Equal(1, summary.Implausible);
            Assert.Equal(1, summary.Unreadable);
            Assert.Equal(1, summary.Missing);

            await using var db = fixture.CreateTargetContext(target);
            var items = await db.MediaItems.ToDictionaryAsync(m => m.Id);
            Assert.Equal(new DateTime(2024, 5, 1, 10, 20, 30), items[withOffset].TakenAtLocal);
            Assert.Equal((short)120, items[withOffset].TakenAtOffsetMinutes);
            Assert.Equal(new DateTime(2019, 8, 17, 21, 5, 9), items[withoutOffset].TakenAtLocal);
            Assert.Null(items[withoutOffset].TakenAtOffsetMinutes);
            foreach (var id in new[] { withoutExif, zeroDate, broken })
            {
                Assert.Null(items[id].TakenAtLocal);
                Assert.NotNull(items[id].CaptureCheckedAt);
            }

            Assert.Null(items[missing].CaptureCheckedAt);
            Assert.Equal(hashes, HashFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GivenAnEarlierRun_WhenTheBackfillRunsAgain_ThenExaminedRowsAreSkippedAndOnlyNewlyAvailableOnesAreRead()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            await AddPhotoAsync(root, target, 74011, "first.jpg", PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09"));
            await AddPhotoAsync(root, target, 74012, "none.jpg", PhotoFixtures.BaseJpeg(64, 48));
            var late = await AddRowAsync(target, 74013, "user_pics/74013/late.jpg");
            Assert.Equal(2, (await RunAsync(root, target)).Examined);

            var second = await RunAsync(root, target);
            Assert.Equal(1, second.Candidates);
            Assert.Equal(0, second.Examined);
            Assert.Equal(1, second.Missing);

            MediaTestSupport.WriteFile(root, 74013, "late.jpg", PhotoFixtures.JpegWithCaptureTime("2020:02:03 04:05:06"));
            var third = await RunAsync(root, target);
            Assert.Equal(1, third.Examined);
            Assert.Equal(1, third.Found);

            var fourth = await RunAsync(root, target);
            Assert.Equal(0, fourth.Candidates);

            await using var db = fixture.CreateTargetContext(target);
            Assert.Equal(new DateTime(2020, 2, 3, 4, 5, 6), (await db.MediaItems.SingleAsync(m => m.Id == late)).TakenAtLocal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GivenRowsThatAlreadyHaveACaptureTimeOrAreNotArchivePhotos_WhenTheBackfillRuns_ThenTheyAreNotCandidates()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var stored = await AddPhotoAsync(root, target, 74021, "stored.jpg", PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09"));
            await using (var db = fixture.CreateTargetContext(target))
            {
                var item = await db.MediaItems.SingleAsync(m => m.Id == stored);
                item.TakenAtLocal = new DateTime(2001, 1, 1);
                await db.SaveChangesAsync();
            }

            await MediaTestSupport.InsertMediaItemAsync(
                fixture, 74022, "forum_attachments/74022/a.jpg", targetConnectionString: target);
            await MediaTestSupport.InsertMediaItemAsync(
                fixture, 74023, "originals/user_pics/74023/v.mp4", Cichlids.Domain.Enums.MediaKind.Video, target);

            var summary = await RunAsync(root, target);

            Assert.Equal(0, summary.Candidates);
            await using var check = fixture.CreateTargetContext(target);
            Assert.Equal(new DateTime(2001, 1, 1), (await check.MediaItems.SingleAsync(m => m.Id == stored)).TakenAtLocal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GivenALargeOriginal_WhenTheBackfillRuns_ThenOnlyTheLeadingBytesAreRead()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            var target = await fixture.CreateIsolatedTargetAsync();
            var exif = new ExifBuilder();
            exif.Exif.Add(exif.Ascii(0x9003, "2019:08:17 21:05:09"));
            using var noise = Image.Gaussnoise(1500, 1500, mean: 128, sigma: 60).Cast(Enums.BandFormat.Uchar);
            var large = MetadataContainers.InsertIntoJpeg(
                noise.WriteToBuffer(".jpg", new VOption { { "Q", 95 } }),
                [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, exif.Build())]);
            await AddPhotoAsync(root, target, 74031, "large.jpg", large);

            var summary = await RunAsync(root, target);

            Assert.Equal(1, summary.Found);
            Assert.True(summary.BytesRead <= Cichlids.Infrastructure.Media.CaptureTimeReader.HeaderBytes);
            Assert.True(large.Length > 2 * summary.BytesRead);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Task<MediaCaptureTimeSummary> RunAsync(string root, string target) =>
        MediaCaptureTimeCommand.BackfillAsync(
            fixture.LegacyConnectionString, target, root, limit: null, workers: 2, maxSourceBytes: 64L * 1024 * 1024, CancellationToken.None);

    private async Task<long> AddPhotoAsync(string root, string target, int uid, string fileName, byte[] bytes)
    {
        var path = MediaTestSupport.WriteFile(root, uid, fileName, bytes);
        return await AddRowAsync(target, uid, path);
    }

    private async Task<long> AddRowAsync(string target, int uid, string path)
    {
        await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
        {
            await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, path);
        }

        return await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{path}", targetConnectionString: target);
    }

    private static Dictionary<string, string> HashFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))));
}
