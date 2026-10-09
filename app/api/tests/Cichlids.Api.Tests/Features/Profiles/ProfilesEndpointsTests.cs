using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Tanks;

namespace Cichlids.Api.Tests.Features.Profiles;

[Collection(ApiCollection.Name)]
public class ProfilesEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetById_ReturnsAvatarFromMediaVariantAndActivityStats()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.Alice.Id}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<ProfileDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("alice", detail.Username);
        Assert.Equal("Berlin", detail.City);
        Assert.Equal("DE", detail.CountryCode);
        Assert.NotNull(detail.AvatarUrl);
        Assert.Contains("avatars/alice", detail.AvatarUrl);
        Assert.Null(detail.ProfileImageUrl);

        // Published posts (any topic) + published tanks + non-deleted comments authored by alice.
        Assert.Equal(3, detail.Stats.PictureCount);
        Assert.Equal(1, detail.Stats.TankCount);
        Assert.Equal(1, detail.Stats.CommentCount);
    }

    [Fact]
    public async Task GetById_FallsBackToExternalAvatarAndResolvesProfileImage()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.Bob.Id}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<ProfileDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("https://example.test/avatars/bob.png", detail.AvatarUrl);
        Assert.NotNull(detail.ProfileImageUrl);
        Assert.Contains("bob-cover", detail.ProfileImageUrl);
    }

    [Fact]
    public async Task GetById_ArchivedProfileReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.ArchivedProfile.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownProfileReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/profiles/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListPictures_IsPreFilteredToTheProfile()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.Alice.Id}/pictures");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<PictureListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(2, body.Total);
        Assert.All(body.Items, i => Assert.Equal(fixture.Seed.Alice.Id, i.Author.Id));
    }

    [Fact]
    public async Task ListTanks_IsPreFilteredToTheProfile()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.Alice.Id}/tanks");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<TankListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.PublishedTankWithExplicitMainImage.Id, body.Items[0].Id);
    }
}
