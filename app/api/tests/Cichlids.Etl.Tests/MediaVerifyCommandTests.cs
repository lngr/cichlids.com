using System.Text.Json;
using Cichlids.Etl.Media;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(MediaCollection.Name)]
public sealed class MediaVerifyCommandTests(MediaEtlFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task FlagsAStorageKeyWhoseObjectWasDeletedAfterSeeding()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 72001;
            var imagePath = MediaTestSupport.WritePhoto(root, uid, "verify-target.jpg", 900, 600);

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, imagePath);
            }

            var mediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{imagePath}");
            var seedExitCode = await MediaTestSupport.RunSeedAsync(fixture, root);
            Assert.Equal(0, seedExitCode);

            string deletedVariantKey;
            await using (var db = fixture.CreateTargetContext())
            {
                var variant = await db.MediaVariants.FirstAsync(v => v.MediaItemId == mediaId);
                deletedVariantKey = variant.StorageKey;
            }

            await fixture.ObjectStore.DeleteAsync(deletedVariantKey);

            var reportPath = Path.Combine(root, "verify-deleted-report.json");
            var exitCode = await MediaVerifyCommand.RunAsync(
                fixture.TargetConnectionString, fixture.ObjectStore, reportPath, CancellationToken.None);

            Assert.Equal(1, exitCode);

            var report = JsonSerializer.Deserialize<ReportDto>(await File.ReadAllTextAsync(reportPath), JsonOptions);
            Assert.NotNull(report);
            var mismatch = Assert.Single(report!.ReferencedMismatches, m => m.StorageKey == deletedVariantKey);
            Assert.Equal("object missing", mismatch.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsNoMismatchesForItsOwnRowsWhenNothingWasDeleted()
    {
        var root = MediaTestSupport.CreateImagesRoot();
        try
        {
            const int uid = 72002;
            var imagePath = MediaTestSupport.WritePhoto(root, uid, "verify-clean.jpg", 500, 400);

            await using (var legacy = await MediaTestSupport.OpenLegacyConnectionAsync(fixture))
            {
                await MediaTestSupport.InsertLegacyPictureAsync(legacy, uid, imagePath);
            }

            var mediaId = await MediaTestSupport.InsertMediaItemAsync(fixture, uid, $"originals/{imagePath}");
            var seedExitCode = await MediaTestSupport.RunSeedAsync(fixture, root);
            Assert.Equal(0, seedExitCode);

            List<string> ownStorageKeys;
            await using (var db = fixture.CreateTargetContext())
            {
                var item = await db.MediaItems.SingleAsync(m => m.Id == mediaId);
                var variantKeys = await db.MediaVariants
                    .Where(v => v.MediaItemId == mediaId)
                    .Select(v => v.StorageKey)
                    .ToListAsync();
                ownStorageKeys = [item.StorageKey, .. variantKeys];
            }

            var reportPath = Path.Combine(root, "verify-clean-report.json");
            await MediaVerifyCommand.RunAsync(fixture.TargetConnectionString, fixture.ObjectStore, reportPath, CancellationToken.None);

            var report = JsonSerializer.Deserialize<ReportDto>(await File.ReadAllTextAsync(reportPath), JsonOptions);
            Assert.NotNull(report);

            // Other tests in this fixture may leave their own rows behind, so the assertion is
            // scoped to this test's own storage keys rather than requiring an empty report.
            Assert.DoesNotContain(report!.ReferencedMismatches, m => ownStorageKeys.Contains(m.StorageKey));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Mirrors the private ReportDto/MismatchDto shape MediaVerifyCommand writes: a separate,
    // test-local copy since the production records are private to that class and the JSON
    // contract, not the CLR type, is what a consumer of --out actually depends on.
    private sealed record MismatchDto(
        string Table, long Id, string StorageKey, long? ExpectedByteSize, long? ActualByteSize, string Reason);

    private sealed record ReportDto(
        DateTimeOffset GeneratedAt, bool Passed, List<MismatchDto> ReferencedMismatches, List<string> OrphanKeys);
}
