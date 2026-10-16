using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Profiles;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

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
    public async Task GetById_FallsBackToExternalAvatarAndResolvesTheProfileImageToItsLargestVariant()
    {
        var response = await fixture.Client.GetAsync($"/api/profiles/{fixture.Seed.Bob.Id}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<ProfileDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("https://example.test/avatars/bob.png", detail.AvatarUrl);
        Assert.Equal($"{ApiFixture.ExpectedPublicUrlPrefix}/variants/medium/profiles/bob-cover.jpg", detail.ProfileImageUrl);
    }

    [Fact]
    public async Task GetById_ResolvesAnAvatarWithOnlyAPublicCopyToTheCopy()
    {
        long profileId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var avatar = new MediaItem { Kind = MediaKind.Photo, StorageKey = "originals/avatars/carol-legacy.jpg", CreatedAt = now };
            db.MediaItems.Add(avatar);
            await db.SaveChangesAsync();
            db.MediaVariants.Add(new MediaVariant
            {
                MediaItemId = avatar.Id, Label = "copy", Width = 64, StorageKey = "variants/copy/originals/avatars/carol-legacy.jpg",
            });
            var carol = new Profile
            {
                Username = $"carol-{Guid.NewGuid():N}"[..20], Kind = ProfileKind.Member, CreatedAt = now,
            };
            db.Profiles.Add(carol);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForProfile(carol.Id, avatar.Id, MediaLinkRole.Avatar));
            await db.SaveChangesAsync();
            profileId = carol.Id;
        }

        var response = await fixture.Client.GetAsync($"/api/profiles/{profileId}");
        response.EnsureSuccessStatusCode();
        var detail = JsonSerializer.Deserialize<ProfileDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal($"{ApiFixture.ExpectedPublicUrlPrefix}/variants/copy/originals/avatars/carol-legacy.jpg", detail.AvatarUrl);
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
