using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Pictures;

[Collection(ApiCollection.Name)]
public class PicturesEndpointsTests(ApiFixture fixture)
{
    private async Task<PagedResponse<PictureListItemDto>> ListAsync(string query)
    {
        var response = await fixture.Client.GetAsync($"/api/pictures{query}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<PagedResponse<PictureListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    [Fact]
    public async Task List_DefaultTopic_ExcludesOfftopicAndNonPublicPosts()
    {
        var body = await ListAsync("");

        var slugs = body.Items.Select(i => i.Slug).ToList();
        Assert.Contains(fixture.Seed.PublishedPictureACanonicalSlug, slugs);
        Assert.Contains(fixture.Seed.PublishedPictureBCanonicalSlug, slugs);
        Assert.Contains(fixture.Seed.PublishedPictureCSlug, slugs);
        Assert.DoesNotContain(fixture.Seed.OfftopicPictureSlug, slugs);
        Assert.DoesNotContain(fixture.Seed.DraftPictureSlug, slugs);
        Assert.DoesNotContain(fixture.Seed.ArchivedPictureSlug, slugs);
        Assert.DoesNotContain(fixture.Seed.DeletedPictureSlug, slugs);
        Assert.Equal(3, body.Total);
    }

    [Fact]
    public async Task List_DefaultSortIsNewestByPublishedAt()
    {
        var body = await ListAsync("");

        Assert.Equal(fixture.Seed.PublishedPictureCSlug, body.Items[0].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureACanonicalSlug, body.Items[1].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureBCanonicalSlug, body.Items[2].Slug);
    }

    [Fact]
    public async Task List_SortViews_OrdersByViewCountDescending()
    {
        var body = await ListAsync("?sort=views");

        Assert.Equal(fixture.Seed.PublishedPictureBCanonicalSlug, body.Items[0].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureACanonicalSlug, body.Items[1].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureCSlug, body.Items[2].Slug);
    }

    [Fact]
    public async Task List_SortRating_UsesBayesianAverageNotRawAverage()
    {
        var body = await ListAsync("?sort=rating");

        // Picture C has the highest raw average (5.0 from a single rating) but Picture A's larger
        // rating count should still win under the Bayesian-shrunk score.
        Assert.Equal(fixture.Seed.PublishedPictureACanonicalSlug, body.Items[0].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureCSlug, body.Items[1].Slug);
        Assert.Equal(fixture.Seed.PublishedPictureBCanonicalSlug, body.Items[2].Slug);
    }

    [Fact]
    public async Task List_FiltersByTopic()
    {
        var body = await ListAsync("?topic=tanks");

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.PublishedPictureBCanonicalSlug, body.Items[0].Slug);
    }

    [Fact]
    public async Task List_FiltersByUser()
    {
        var body = await ListAsync($"?user={fixture.Seed.Alice.Id}");

        Assert.Equal(2, body.Total);
        Assert.All(body.Items, i => Assert.Equal(fixture.Seed.Alice.Id, i.Author.Id));
    }

    [Fact]
    public async Task List_CapsLimitAtFifty()
    {
        var body = await ListAsync("?limit=999");

        Assert.True(body.Items.Count <= Pagination.MaxLimit);
    }

    [Fact]
    public async Task List_UnknownSortReturnsBadRequest()
    {
        var response = await fixture.Client.GetAsync("/api/pictures?sort=popularity");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_UnknownTopicReturnsBadRequest()
    {
        var response = await fixture.Client.GetAsync("/api/pictures?topic=nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ItemIncludesResolvedVariantUrlsAndAuthor()
    {
        var body = await ListAsync("");
        var pictureA = body.Items.Single(i => i.Slug == fixture.Seed.PublishedPictureACanonicalSlug);

        Assert.Equal(PostTopic.Cichlids, pictureA.Topic);
        Assert.Equal(fixture.Seed.Alice.Id, pictureA.Author.Id);
        Assert.Equal("alice", pictureA.Author.Username);
        Assert.NotNull(pictureA.Image);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, pictureA.Image!.Thumb);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, pictureA.Image.Small);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, pictureA.Image.Medium);
        Assert.Null(pictureA.Image.Large);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, pictureA.Image.Original);
    }

    [Fact]
    public async Task GetBySlug_ResolvesCanonicalSlugAndIncrementsViewCount()
    {
        var first = await fixture.Client.GetAsync($"/api/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}");
        first.EnsureSuccessStatusCode();
        var firstDetail = JsonSerializer.Deserialize<PictureDetailDto>(await first.Content.ReadAsStringAsync(), TestJson.Options)!;

        var second = await fixture.Client.GetAsync($"/api/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}");
        second.EnsureSuccessStatusCode();
        var secondDetail = JsonSerializer.Deserialize<PictureDetailDto>(await second.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(fixture.Seed.PublishedPictureACanonicalSlug, firstDetail.CanonicalSlug);
        Assert.Equal(firstDetail.ViewCount + 1, secondDetail.ViewCount);
    }

    [Fact]
    public async Task GetBySlug_NonCanonicalAliasResolvesToSamePostAndReportsCanonicalSlug()
    {
        var response = await fixture.Client.GetAsync($"/api/pictures/{fixture.Seed.PublishedPictureAAltSlug}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<PictureDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(fixture.Seed.PublishedPictureA.Id, detail.Id);
        Assert.Equal(fixture.Seed.PublishedPictureACanonicalSlug, detail.CanonicalSlug);
    }

    [Theory]
    [InlineData(nameof(SeedData.DraftPictureSlug))]
    [InlineData(nameof(SeedData.ArchivedPictureSlug))]
    [InlineData(nameof(SeedData.DeletedPictureSlug))]
    public async Task GetBySlug_NonPublicPostReturnsNotFound(string seedSlugProperty)
    {
        var slug = seedSlugProperty switch
        {
            nameof(SeedData.DraftPictureSlug) => fixture.Seed.DraftPictureSlug,
            nameof(SeedData.ArchivedPictureSlug) => fixture.Seed.ArchivedPictureSlug,
            nameof(SeedData.DeletedPictureSlug) => fixture.Seed.DeletedPictureSlug,
            _ => throw new ArgumentOutOfRangeException(nameof(seedSlugProperty)),
        };

        var response = await fixture.Client.GetAsync($"/api/pictures/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBySlug_UnknownSlugReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/pictures/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
