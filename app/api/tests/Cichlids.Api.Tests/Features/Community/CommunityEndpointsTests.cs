using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Community;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Community;

[Collection(ApiCollection.Name)]
public class CommunityEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Categories_ReturnsAllThreeCategoriesWithCountsAndLastPostAt()
    {
        var response = await fixture.Client.GetAsync("/api/community/categories");
        response.EnsureSuccessStatusCode();
        var body = JsonSerializer.Deserialize<List<CommunityCategoryDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(3, body.Count);

        var cichlids = body.Single(c => c.Category == DiscussionCategory.Cichlids);
        Assert.Equal(2, cichlids.ThreadCount);
        Assert.Equal(3, cichlids.PostCount);
        AssertCloseEnough(fixture.Seed.CommunityCichlidsThreadB.LastPostAt, cichlids.LastPostAt);

        var african = body.Single(c => c.Category == DiscussionCategory.African);
        Assert.Equal(1, african.ThreadCount);
        Assert.Equal(1, african.PostCount);
        AssertCloseEnough(fixture.Seed.CommunityAfricanThreadA.LastPostAt, african.LastPostAt);

        var marketPlace = body.Single(c => c.Category == DiscussionCategory.MarketPlace);
        Assert.Equal(1, marketPlace.ThreadCount);
        Assert.Equal(0, marketPlace.PostCount);
        Assert.Null(marketPlace.LastPostAt);
    }

    [Fact]
    public async Task Threads_DefaultSortIsLastPostAtDescendingAcrossCategories()
    {
        var body = await ListThreadsAsync("");

        var ids = body.Items.Select(i => i.Id).ToList();
        var expectedOrder = new[]
        {
            fixture.Seed.CommunityCichlidsThreadB.Id,
            fixture.Seed.CommunityAfricanThreadA.Id,
            fixture.Seed.CommunityCichlidsThreadA.Id,
        };
        Assert.Equal(expectedOrder, ids.Where(expectedOrder.Contains).ToList());
    }

    [Fact]
    public async Task Threads_WithNoLastPostAtSortsLastDespiteBeingMostRecentlyCreated()
    {
        var body = await ListThreadsAsync("");

        Assert.Equal(fixture.Seed.CommunityMarketPlaceThreadWithNoPosts.Id, body.Items[^1].Id);
    }

    [Fact]
    public async Task Threads_FiltersByCategory()
    {
        var body = await ListThreadsAsync("?category=african");

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.CommunityAfricanThreadA.Id, body.Items[0].Id);
        Assert.Equal(DiscussionCategory.African, body.Items[0].Category);
    }

    [Fact]
    public async Task Threads_UnknownCategoryReturnsBadRequest()
    {
        var response = await fixture.Client.GetAsync("/api/community/threads?category=nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Threads_QueryFiltersTitleCaseInsensitively()
    {
        var body = await ListThreadsAsync("?query=SWINGS");

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.CommunityCichlidsThreadB.Id, body.Items[0].Id);
    }

    [Fact]
    public async Task Threads_StartedByIsProfileRefForARealMember()
    {
        var body = await ListThreadsAsync("");
        var threadA = body.Items.Single(i => i.Id == fixture.Seed.CommunityCichlidsThreadA.Id);

        Assert.NotNull(threadA.StartedBy.ProfileRef);
        Assert.Equal(fixture.Seed.Alice.Id, threadA.StartedBy.ProfileRef!.Id);
        Assert.Equal(fixture.Seed.Alice.Username, threadA.StartedBy.ProfileRef.Username);
        Assert.Null(threadA.StartedBy.PosterName);
    }

    [Fact]
    public async Task Threads_StartedByIsPosterNameForAnUnlistedPlaceholderProfile()
    {
        var body = await ListThreadsAsync("");
        var threadB = body.Items.Single(i => i.Id == fixture.Seed.CommunityCichlidsThreadB.Id);

        Assert.Null(threadB.StartedBy.ProfileRef);
        Assert.Equal(fixture.Seed.ArchivedProfile.DisplayName, threadB.StartedBy.PosterName);
    }

    /// <summary>
    /// Postgres' timestamptz has microsecond precision, one digit coarser than a .NET
    /// <see cref="DateTimeOffset"/> tick, so a seeded value that round-tripped through the
    /// database can be up to a tick off from the in-memory value the test seeded it with.
    /// </summary>
    private static void AssertCloseEnough(DateTimeOffset? expected, DateTimeOffset? actual) =>
        Assert.True(
            (expected - actual) is { } delta && delta.Duration() < TimeSpan.FromMilliseconds(1),
            $"Expected {expected:O} to be within 1ms of {actual:O}.");

    private async Task<PagedResponse<CommunityThreadListItemDto>> ListThreadsAsync(string query)
    {
        var response = await fixture.Client.GetAsync($"/api/community/threads{query}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<PagedResponse<CommunityThreadListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    [Fact]
    public async Task ThreadDetail_ReturnsPostsInOrderWithStateAndCounts()
    {
        var detail = await GetThreadAsync(fixture.Seed.CommunityCichlidsThreadA.Id);

        Assert.Equal(fixture.Seed.CommunityCichlidsThreadA.Title, detail.Title);
        Assert.Equal(DiscussionCategory.Cichlids, detail.Category);
        Assert.Equal(DiscussionThreadState.Archived, detail.State);
        Assert.Equal(2, detail.PostCount);
        Assert.Equal(2, detail.Posts.Count);
        Assert.Equal(fixture.Seed.CommunityCichlidsThreadAMemberPost.Id, detail.Posts[0].Id);
        Assert.Equal(fixture.Seed.CommunityCichlidsThreadAGuestPost.Id, detail.Posts[1].Id);
        Assert.True(detail.Posts[0].CreatedAt <= detail.Posts[1].CreatedAt);
    }

    [Fact]
    public async Task ThreadDetail_MemberPostAuthorHasFullProfileFields()
    {
        var detail = await GetThreadAsync(fixture.Seed.CommunityCichlidsThreadA.Id);
        var memberPost = detail.Posts.Single(p => p.Id == fixture.Seed.CommunityCichlidsThreadAMemberPost.Id);

        Assert.Equal(fixture.Seed.Alice.Id, memberPost.Author.Id);
        Assert.Equal(fixture.Seed.Alice.Username, memberPost.Author.Username);
        Assert.Equal(fixture.Seed.Alice.DisplayName, memberPost.Author.DisplayName);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, memberPost.Author.AvatarUrl);
    }

    [Fact]
    public async Task ThreadDetail_GuestPostAuthorIsPosterNameOnly()
    {
        var detail = await GetThreadAsync(fixture.Seed.CommunityCichlidsThreadA.Id);
        var guestPost = detail.Posts.Single(p => p.Id == fixture.Seed.CommunityCichlidsThreadAGuestPost.Id);

        Assert.Null(guestPost.Author.Id);
        Assert.Null(guestPost.Author.Username);
        Assert.Null(guestPost.Author.AvatarUrl);
        Assert.Equal(fixture.Seed.CommunityCichlidsThreadAGuestPost.PosterName, guestPost.Author.DisplayName);
    }

    [Fact]
    public async Task ThreadDetail_PlaceholderProfilePostAuthorIsDisplayNameOnly()
    {
        var detail = await GetThreadAsync(fixture.Seed.CommunityCichlidsThreadB.Id);
        var placeholderPost = detail.Posts.Single(p => p.Id == fixture.Seed.CommunityCichlidsThreadBPlaceholderPost.Id);

        Assert.Null(placeholderPost.Author.Id);
        Assert.Null(placeholderPost.Author.Username);
        Assert.Null(placeholderPost.Author.AvatarUrl);
        Assert.Equal(fixture.Seed.ArchivedProfile.DisplayName, placeholderPost.Author.DisplayName);
    }

    [Fact]
    public async Task ThreadDetail_IncludesAttachmentWithResolvedImageUrls()
    {
        var detail = await GetThreadAsync(fixture.Seed.CommunityCichlidsThreadA.Id);
        var guestPost = detail.Posts.Single(p => p.Id == fixture.Seed.CommunityCichlidsThreadAGuestPost.Id);
        var memberPost = detail.Posts.Single(p => p.Id == fixture.Seed.CommunityCichlidsThreadAMemberPost.Id);

        Assert.Empty(memberPost.Attachments);
        Assert.Single(guestPost.Attachments);
        var attachment = guestPost.Attachments[0];
        Assert.Equal(fixture.Seed.CommunityCichlidsThreadAGuestPostAttachmentMediaItemId, attachment.MediaItemId);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, attachment.Image.Thumb);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, attachment.Image.Small);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, attachment.Image.Original);
    }

    [Fact]
    public async Task ThreadDetail_UnknownIdReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/community/threads/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoResponseEverExposesAnEmailAddress()
    {
        var categories = await fixture.Client.GetAsync("/api/community/categories");
        var threads = await fixture.Client.GetAsync("/api/community/threads");
        var detail = await fixture.Client.GetAsync($"/api/community/threads/{fixture.Seed.CommunityCichlidsThreadA.Id}");

        Assert.DoesNotContain('@', await categories.Content.ReadAsStringAsync());
        Assert.DoesNotContain('@', await threads.Content.ReadAsStringAsync());
        Assert.DoesNotContain('@', await detail.Content.ReadAsStringAsync());
    }

    private async Task<CommunityThreadDetailDto> GetThreadAsync(long id)
    {
        var response = await fixture.Client.GetAsync($"/api/community/threads/{id}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<CommunityThreadDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }
}
