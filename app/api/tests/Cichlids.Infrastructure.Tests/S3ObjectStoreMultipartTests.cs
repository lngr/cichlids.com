using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

[Trait("Category", "Docker")]
[Collection(RustFsCollection.Name)]
public sealed class S3ObjectStoreMultipartTests(RustFsFixture fixture)
{
    private const int PartSize = 5 * 1024 * 1024;

    private static byte[] Random(int length)
    {
        var bytes = new byte[length];
        System.Random.Shared.NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task ACompletedMultipartUploadStoresTheConcatenatedParts()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();
        var first = Random(PartSize);
        var last = Random(1234);

        var uploadId = await store.BeginMultipartUploadAsync("private/video/1/a.mp4", "video/mp4");
        var etags = new List<MultipartPart>
        {
            new(1, await store.UploadPartAsync("private/video/1/a.mp4", uploadId, 1, first)),
            new(2, await store.UploadPartAsync("private/video/1/a.mp4", uploadId, 2, last)),
        };
        await store.CompleteMultipartUploadAsync("private/video/1/a.mp4", uploadId, etags);

        var head = await store.HeadAsync("private/video/1/a.mp4");
        Assert.Equal(PartSize + 1234, head!.Size);
        using var downloaded = new MemoryStream();
        Assert.True(await store.DownloadAsync("private/video/1/a.mp4", downloaded, 10 * 1024 * 1024));
        Assert.Equal([.. first, .. last], downloaded.ToArray());
    }

    [Fact]
    public async Task APartUploadedTwiceKeepsTheLastContent()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();
        var uploadId = await store.BeginMultipartUploadAsync("b.mp4", "video/mp4");
        await store.UploadPartAsync("b.mp4", uploadId, 1, Random(100));
        var replacement = Random(100);
        var etag = await store.UploadPartAsync("b.mp4", uploadId, 1, replacement);

        await store.CompleteMultipartUploadAsync("b.mp4", uploadId, [new MultipartPart(1, etag)]);

        using var downloaded = new MemoryStream();
        await store.DownloadAsync("b.mp4", downloaded, 1024);
        Assert.Equal(replacement, downloaded.ToArray());
    }

    [Fact]
    public async Task AnAbortedMultipartUploadLeavesNoObject()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();
        var uploadId = await store.BeginMultipartUploadAsync("c.mp4", "video/mp4");
        await store.UploadPartAsync("c.mp4", uploadId, 1, Random(100));

        await store.AbortMultipartUploadAsync("c.mp4", uploadId);

        Assert.False(await store.ExistsAsync("c.mp4"));
        await store.AbortMultipartUploadAsync("c.mp4", uploadId);
    }

    [Fact]
    public async Task DownloadReadsAnObjectBeyondTheGetLimitUpToItsOwnCap()
    {
        var store = await fixture.CreateStoreForNewBucketAsync(maxDownloadBytes: 1024);
        var content = Random(4096);
        await store.PutAsync("big.bin", new MemoryStream(content), "application/octet-stream");

        using var downloaded = new MemoryStream();
        Assert.True(await store.DownloadAsync("big.bin", downloaded, 4096));

        Assert.Equal(content, downloaded.ToArray());
    }

    [Fact]
    public async Task DownloadRefusesAnObjectLargerThanItsCapWithoutCopyingIt()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();
        await store.PutAsync("huge.bin", new MemoryStream(Random(4097)), "application/octet-stream");

        using var downloaded = new MemoryStream();
        var error = await Assert.ThrowsAsync<ObjectTooLargeException>(() => store.DownloadAsync("huge.bin", downloaded, 4096));

        Assert.Equal(4096, error.MaxBytes);
        Assert.Equal(0, downloaded.Length);
    }

    [Fact]
    public async Task APartLargerThanThePartLimitIsRefusedBeforeAnyRequest()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();
        var uploadId = await store.BeginMultipartUploadAsync("d.mp4", "video/mp4");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.UploadPartAsync("d.mp4", uploadId, 1, new byte[S3ObjectStore.MaxPartBytes + 1]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.UploadPartAsync("d.mp4", uploadId, 0, new byte[10]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.UploadPartAsync("d.mp4", uploadId, S3ObjectStore.MaxParts + 1, new byte[10]));
    }

    [Fact]
    public async Task DownloadReportsAMissingObject()
    {
        var store = await fixture.CreateStoreForNewBucketAsync();

        using var downloaded = new MemoryStream();
        Assert.False(await store.DownloadAsync("missing.bin", downloaded, 1024));
    }
}
