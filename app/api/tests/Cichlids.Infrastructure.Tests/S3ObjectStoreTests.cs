using System.Net;
using System.Text;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

[Trait("Category", "Docker")]
[Collection(RustFsCollection.Name)]
public sealed class S3ObjectStoreTests
{
    private readonly RustFsFixture _fixture;

    public S3ObjectStoreTests(RustFsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutAsync_then_GetAsync_returns_the_same_content()
    {
        var store = _fixture.CreateStoreForNewBucket();
        var content = Encoding.UTF8.GetBytes("cichlids swim in schools");

        await store.PutAsync("images/original.jpg", new MemoryStream(content), "image/jpeg");

        await using var result = await store.GetAsync("images/original.jpg");
        Assert.NotNull(result);
        using var buffer = new MemoryStream();
        await result!.CopyToAsync(buffer);
        Assert.Equal(content, buffer.ToArray());
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_missing_key()
    {
        var store = _fixture.CreateStoreForNewBucket();

        var result = await store.GetAsync("does/not/exist.jpg");

        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsAsync_reflects_whether_the_key_was_put()
    {
        var store = _fixture.CreateStoreForNewBucket();

        Assert.False(await store.ExistsAsync("images/present.jpg"));

        await store.PutAsync("images/present.jpg", new MemoryStream([1, 2, 3]), "application/octet-stream");

        Assert.True(await store.ExistsAsync("images/present.jpg"));
    }

    [Fact]
    public async Task GetSizeAsync_returns_the_byte_length_of_an_existing_object()
    {
        var store = _fixture.CreateStoreForNewBucket();
        var content = Encoding.UTF8.GetBytes("cichlids swim in schools");

        await store.PutAsync("images/sized.jpg", new MemoryStream(content), "image/jpeg");

        var size = await store.GetSizeAsync("images/sized.jpg");

        Assert.Equal(content.LongLength, size);
    }

    [Fact]
    public async Task GetSizeAsync_returns_null_for_a_missing_key()
    {
        var store = _fixture.CreateStoreForNewBucket();

        var size = await store.GetSizeAsync("does/not/exist.jpg");

        Assert.Null(size);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_object()
    {
        var store = _fixture.CreateStoreForNewBucket();
        await store.PutAsync("images/to-delete.jpg", new MemoryStream([1, 2, 3]), "application/octet-stream");
        Assert.True(await store.ExistsAsync("images/to-delete.jpg"));

        await store.DeleteAsync("images/to-delete.jpg");

        Assert.False(await store.ExistsAsync("images/to-delete.jpg"));
    }

    [Fact]
    public async Task ListKeysAsync_returns_every_key_across_more_than_one_provider_page()
    {
        // A small page size forces the SDK to hand back multiple result pages for five objects,
        // exercising the continuation-token loop without uploading thousands of objects.
        var store = _fixture.CreateStoreForNewBucket(listPageSize: 2);
        var expectedKeys = Enumerable.Range(1, 5).Select(i => $"gallery/photo-{i}.jpg").ToArray();

        foreach (var key in expectedKeys)
        {
            await store.PutAsync(key, new MemoryStream([1]), "application/octet-stream");
        }

        var listedKeys = new List<string>();
        await foreach (var key in store.ListKeysAsync("gallery/"))
        {
            listedKeys.Add(key);
        }

        Assert.Equal(expectedKeys.OrderBy(k => k), listedKeys.OrderBy(k => k));
    }

    [Fact]
    public async Task ListKeysAsync_returns_no_keys_for_a_prefix_with_no_matching_objects()
    {
        var store = _fixture.CreateStoreForNewBucket();

        var listedKeys = new List<string>();
        await foreach (var key in store.ListKeysAsync("empty-prefix/"))
        {
            listedKeys.Add(key);
        }

        Assert.Empty(listedKeys);
    }

    [Fact]
    public async Task EnsurePublicReadPolicyAsync_makes_an_existing_object_anonymously_readable()
    {
        var store = _fixture.CreateStoreForNewBucket();
        await store.PutAsync("originals/public-read.jpg", new MemoryStream([1, 2, 3]), "image/jpeg");

        var applied = await store.EnsurePublicReadPolicyAsync();
        Assert.True(applied);

        using var anonymousClient = new HttpClient();
        var response = await anonymousClient.GetAsync($"{_fixture.ServiceUrl}/{store.Bucket}/originals/public-read.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EnsurePublicReadPolicyAsync_leaves_a_missing_key_reported_as_not_found()
    {
        var store = _fixture.CreateStoreForNewBucket();

        var applied = await store.EnsurePublicReadPolicyAsync();
        Assert.True(applied);

        using var anonymousClient = new HttpClient();
        var response = await anonymousClient.GetAsync($"{_fixture.ServiceUrl}/{store.Bucket}/originals/does-not-exist.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
