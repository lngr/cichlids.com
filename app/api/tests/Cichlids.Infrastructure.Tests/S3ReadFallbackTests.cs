using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

[Collection(RustFsCollection.Name)]
public class S3ReadFallbackTests(RustFsFixture fixture)
{
    [Fact]
    public async Task Missing_environment_object_resolves_to_golden_and_environment_writes_take_precedence()
    {
        var store = fixture.CreateStoreForNewBucket(readFallbackPrefix: "golden/v1/media/");
        const string key = "variants/thumb/media-1.jpg";
        const string golden = "golden/v1/media/" + key;
        await SeedAsync(store, golden);

        Assert.True(await store.ExistsAsync(key));
        Assert.Equal(3, await store.GetSizeAsync(key));
        Assert.Equal("base", (await store.HeadAsync(key))!.Metadata["source"]);
        await using (var body = await store.GetAsync(key))
        {
            Assert.NotNull(body);
            Assert.Equal(1, body.ReadByte());
        }
        Assert.EndsWith(golden, store.GetPublicUrl(key));

        await store.PutAsync(key, new MemoryStream([9]), "image/jpeg");
        Assert.Equal(1, await store.GetSizeAsync(key));
        Assert.Empty((await store.HeadAsync(key))!.Metadata);
        await using (var body = await store.GetAsync(key))
        {
            Assert.Equal(9, body!.ReadByte());
        }
        Assert.EndsWith("/media/" + key, store.GetPublicUrl(key));
        await store.DeleteAsync(key);
        Assert.Equal(3, await store.GetSizeAsync(key));
        Assert.Equal(3, await store.GetSizeAsync(golden));
        Assert.Null(await store.GetAsync("missing"));
        Assert.Null(await store.HeadAsync("missing"));
        Assert.False(await store.ExistsAsync("missing"));
        Assert.Null(await store.GetSizeAsync("missing"));
        var keys = new List<string>();
        await foreach (var listed in store.ListKeysAsync("")) keys.Add(listed);
        Assert.DoesNotContain(golden, keys);
    }

    [Fact]
    public async Task Empty_option_uses_environment_keys_only()
    {
        var store = fixture.CreateStoreForNewBucket();
        const string key = "variants/thumb/media-1.jpg";
        await SeedAsync(store, "golden/v1/media/" + key);
        Assert.False(await store.ExistsAsync(key));
        Assert.Null(await store.HeadAsync(key));
        Assert.Null(await store.GetAsync(key));
        Assert.Null(await store.GetSizeAsync(key));
        Assert.EndsWith("/media/" + key, store.GetPublicUrl(key));
    }

    [Fact]
    public async Task Golden_keys_reject_direct_writes_and_deletes()
    {
        var store = fixture.CreateStoreForNewBucket(readFallbackPrefix: "golden/v1/media/");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("golden/v1/media/a", new MemoryStream([1]), "image/jpeg"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("golden/v1/media/a"));
    }

    [Fact]
    public async Task Environment_prefix_isolates_writes_and_listings()
    {
        var store = fixture.CreateStoreForNewBucket(readFallbackPrefix: "golden/v1/media/", writePrefix: "environments/staging/");
        const string key = "variants/thumb/media-1.jpg";
        await SeedAsync(store, "golden/v1/media/" + key);
        Assert.Equal(3, await store.GetSizeAsync(key));
        Assert.EndsWith("golden/v1/media/" + key, store.GetPublicUrl(key));
        await store.PutAsync(key, new MemoryStream([9]), "image/jpeg");
        Assert.Equal(1, await store.GetSizeAsync(key));
        Assert.EndsWith("environments/staging/" + key, store.GetPublicUrl(key));
        var keys = new List<string>();
        await foreach (var listed in store.ListKeysAsync("variants/")) keys.Add(listed);
        Assert.Equal([key], keys);
        using var client = fixture.CreateClient();
        Assert.Equal(1, (await client.GetObjectMetadataAsync(store.Bucket, "environments/staging/" + key)).ContentLength);
        Assert.Equal(3, (await client.GetObjectMetadataAsync(store.Bucket, "golden/v1/media/" + key)).ContentLength);
        await store.DeleteAsync(key);
        Assert.Equal(3, await store.GetSizeAsync(key));
    }

    [Fact]
    public async Task Fallback_obeys_download_cap()
    {
        var store = fixture.CreateStoreForNewBucket(maxDownloadBytes: 2, readFallbackPrefix: "golden/v1/media/");
        await SeedAsync(store, "golden/v1/media/variants/thumb/media-1.jpg");
        await Assert.ThrowsAsync<ObjectTooLargeException>(() => store.GetAsync("variants/thumb/media-1.jpg"));
    }

    private async Task SeedAsync(S3ObjectStore store, string golden)
    {
        using var client = fixture.CreateClient();
        await client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = store.Bucket,
            Key = golden,
            InputStream = new MemoryStream([1, 2, 3]),
            ContentType = "image/jpeg",
            Metadata = { ["source"] = "base" },
        });
    }
}
