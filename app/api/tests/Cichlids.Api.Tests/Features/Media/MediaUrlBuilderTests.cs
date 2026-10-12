using Cichlids.Api.Features.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Tests.Features.Media;

public class MediaUrlBuilderTests
{
    private const string BaseUrl = "https://media.test";

    private readonly MediaUrlBuilder _builder = new(new UrlOnlyObjectStore());

    [Fact]
    public void Build_resolves_each_variant_label_and_leaves_missing_ones_null()
    {
        var image = _builder.Build(new Dictionary<string, string>
        {
            ["thumb"] = "variants/thumb/originals/a.jpg.jpg",
            ["medium"] = "variants/medium/originals/a.jpg.jpg",
        });

        Assert.Equal($"{BaseUrl}/variants/thumb/originals/a.jpg.jpg", image.Thumb);
        Assert.Null(image.Small);
        Assert.Equal($"{BaseUrl}/variants/medium/originals/a.jpg.jpg", image.Medium);
        Assert.Null(image.Large);
    }

    [Fact]
    public void Build_without_variants_resolves_every_size_to_null()
    {
        var image = _builder.Build(new Dictionary<string, string>());

        Assert.Equal(new ImageUrlsDto(null, null, null, null), image);
    }

    [Theory]
    [InlineData("originals/a.jpg")]
    [InlineData("originals/variants/a.jpg")]
    [InlineData("other/a.jpg")]
    public void Build_refuses_a_key_outside_the_public_prefixes(string storageKey)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => _builder.Build(new Dictionary<string, string> { ["thumb"] = storageKey }));

        Assert.Contains(storageKey, exception.Message);
    }

    [Fact]
    public void BuildBest_returns_the_largest_variant()
    {
        var url = _builder.BuildBest(new Dictionary<string, string>
        {
            ["thumb"] = "variants/thumb/originals/a.jpg.jpg",
            ["small"] = "variants/small/originals/a.jpg.jpg",
            ["medium"] = "variants/medium/originals/a.jpg.jpg",
        });

        Assert.Equal($"{BaseUrl}/variants/medium/originals/a.jpg.jpg", url);
    }

    [Fact]
    public void BuildBest_without_variants_returns_null()
    {
        Assert.Null(_builder.BuildBest(new Dictionary<string, string>()));
    }

    [Fact]
    public void BuildBest_refuses_a_key_outside_the_public_prefixes()
    {
        Assert.Throws<InvalidOperationException>(
            () => _builder.BuildBest(new Dictionary<string, string> { ["large"] = "originals/a.jpg" }));
    }

    [Fact]
    public void Build_resolves_every_size_to_the_public_copy_of_an_item_without_sized_variants()
    {
        var image = _builder.Build(new Dictionary<string, string> { ["copy"] = "variants/copy/originals/a.jpg" });

        var copy = $"{BaseUrl}/variants/copy/originals/a.jpg";
        Assert.Equal(new ImageUrlsDto(copy, copy, copy, copy), image);
    }

    [Fact]
    public void Build_prefers_sized_variants_over_the_public_copy()
    {
        var image = _builder.Build(new Dictionary<string, string>
        {
            ["thumb"] = "variants/thumb/originals/a.jpg.jpg",
            ["copy"] = "variants/copy/originals/a.jpg",
        });

        Assert.Equal(new ImageUrlsDto($"{BaseUrl}/variants/thumb/originals/a.jpg.jpg", null, null, null), image);
    }

    [Fact]
    public void BuildBest_falls_back_to_the_public_copy()
    {
        Assert.Equal(
            $"{BaseUrl}/variants/copy/originals/a.jpg",
            _builder.BuildBest(new Dictionary<string, string> { ["copy"] = "variants/copy/originals/a.jpg" }));
    }

    /// <summary>
    /// An object store that only builds public URLs; every storage operation fails, since URL
    /// building never reaches the store.
    /// </summary>
    private sealed class UrlOnlyObjectStore : IObjectStore
    {
        public string GetPublicUrl(string key) => $"{BaseUrl}/{key}";

        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PutAsync(
            string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
