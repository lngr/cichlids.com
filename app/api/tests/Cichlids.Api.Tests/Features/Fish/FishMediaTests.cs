// Story: TASK-9.2
using System.Net;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Fish;

/// <summary>
/// The photos and videos of a named fish: the owner links files of the own media library, a file of
/// another member is refused, and the files follow the visibility of the tank the fish lives in.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-9.2")]
public sealed class FishMediaTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly FishWorld _world = new(fixture);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _world.CleanAsync();

    [Fact]
    public async Task GivenFilesOfTheLibrary_WhenTheOwnerLinksThem_ThenThePageListsPhotosAndVideosNewestFirst()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var photoOld = await _world.NewMediaAsync(owner.ProfileId, createdAt: DateTimeOffset.UtcNow.AddHours(-3));
        var photoNew = await _world.NewMediaAsync(owner.ProfileId, createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        var videoOld = await _world.NewMediaAsync(owner.ProfileId, MediaKind.Video, DateTimeOffset.UtcNow.AddHours(-4));
        var videoNew = await _world.NewMediaAsync(owner.ProfileId, MediaKind.Video, DateTimeOffset.UtcNow.AddHours(-2));
        foreach (var item in new[] { photoOld, videoOld, photoNew, videoNew }) await _world.LinkAsync(owner, fish, item);

        var page = await _world.GetAsync(fish, owner.Token);

        Assert.Equal([photoNew, photoOld], page.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("mediaItemId").GetInt64()));
        Assert.Equal([videoNew, videoOld], page.GetProperty("videos").EnumerateArray().Select(v => v.GetProperty("id").GetInt64()));
        await using var db = fixture.CreateDbContext();
        var links = await db.MediaLinks.Where(l => l.InhabitantId == fish).ToListAsync();
        Assert.All(links, l => Assert.Equal(MediaLinkContext.Inhabitant, l.ContextType));
        Assert.Equal(2, links.Count(l => l.Role == MediaLinkRole.Photo));
        Assert.Equal(2, links.Count(l => l.Role == MediaLinkRole.Video));
    }

    [Fact]
    public async Task GivenAFileOfAnotherMemberOrAMissingFile_WhenTheOwnerLinksIt_ThenTheApiAnswersNotFoundAndLinksNothing()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var other = await NewMemberAsync(fixture);
        var foreign = await _world.NewMediaAsync(other.ProfileId);

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/media", owner.Token, new { mediaItemId = foreign }, HttpStatusCode.NotFound, "media_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/media", owner.Token, new { mediaItemId = long.MaxValue }, HttpStatusCode.NotFound, "media_not_found");
        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/media", owner.Token, new { }, HttpStatusCode.BadRequest, "fish_invalid");

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.MediaLinks.Where(l => l.InhabitantId == fish).ToListAsync());
    }

    [Fact]
    public async Task GivenALinkedFile_WhenLinkingItAgain_ThenTheApiAnswersConflict()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await _world.LinkAsync(owner, fish, photo);

        await _world.AssertRefusedAsync(HttpMethod.Post, fish, "/media", owner.Token, new { mediaItemId = photo }, HttpStatusCode.Conflict, "media_already_linked");
    }

    [Fact]
    public async Task GivenALinkedFile_WhenTheOwnerRemovesTheLink_ThenTheFileStaysInTheLibrary()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await _world.LinkAsync(owner, fish, photo);

        using var response = await SendAsync(fixture, HttpMethod.Delete, FishWorld.Url(fish, $"/media/{photo}"), owner.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await BodyAsync(response)).GetProperty("photos").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.MediaItems.AnyAsync(m => m.Id == photo));
        await _world.AssertRefusedAsync(HttpMethod.Delete, fish, $"/media/{photo}", owner.Token, null, HttpStatusCode.NotFound, "media_not_found");
    }

    [Fact]
    public async Task GivenAPhotoOfAFishInAPublicTank_WhenTheFishMovesToAPrivateTank_ThenOthersNoLongerSeeThePhotoAndSignedLinksExpire()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var target = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private);
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await _world.LinkAsync(owner, fish, photo);
        var stranger = await NewMemberAsync(fixture);
        long before;
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Contains(photo, await db.VisibleMediaItemIdsAsync([photo], stranger.ProfileId, DateTimeOffset.UtcNow));
            before = (await db.MediaItems.SingleAsync(m => m.Id == photo)).VisibilityGeneration;
        }

        await _world.PostAsync(fish, "/move", owner, new { toTankId = target });

        await using var check = fixture.CreateDbContext();
        Assert.DoesNotContain(photo, await check.VisibleMediaItemIdsAsync([photo], stranger.ProfileId, DateTimeOffset.UtcNow));
        Assert.Contains(photo, await check.VisibleMediaItemIdsAsync([photo], owner.ProfileId, DateTimeOffset.UtcNow));
        var item = await check.MediaItems.SingleAsync(m => m.Id == photo);
        Assert.True(item.VisibilityGeneration > before);
    }

    [Fact]
    public async Task GivenAFileOnTheFishPage_WhenTheOwnerReadsItsPlaces_ThenThePlaceNamesTheFishAndSurvivesTheDeletionOfTheTank()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await _world.LinkAsync(owner, fish, photo);

        async Task<JsonElement> PlaceAsync()
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/me/media/{photo}", owner.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single((await BodyAsync(response)).GetProperty("places").EnumerateArray());
        }

        var place = await PlaceAsync();
        Assert.Equal("inhabitant", place.GetProperty("kind").GetString());
        Assert.Equal("Bruno", place.GetProperty("name").GetString());
        Assert.Equal(tank, place.GetProperty("tankId").GetInt64());
        await _world.SoftDeleteTankAsync(tank);
        Assert.Equal("inhabitant", (await PlaceAsync()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GivenAnInhabitantLink_WhenItHasATankRole_ThenTheDatabaseRefusesIt()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        var photo = await _world.NewMediaAsync(owner.ProfileId);
        await using var db = fixture.CreateDbContext();
        db.MediaLinks.Add(Cichlids.Domain.Entities.MediaLink.ForInhabitant(fish, photo, MediaLinkRole.Showcase));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task GivenAMemorial_WhenTheOwnerAddsAPhoto_ThenItIsLinked()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.PostAsync(fish, "/death", owner, new { });
        var photo = await _world.NewMediaAsync(owner.ProfileId);

        await _world.LinkAsync(owner, fish, photo);

        Assert.Single((await _world.GetAsync(fish, null)).GetProperty("photos").EnumerateArray());
    }

    [Fact]
    public async Task GivenAProvisionalOwner_WhenLinkingAPhoto_ThenTheFileAppearsOnItsOwnPage()
    {
        using var caller = await Provisional.ProvisionalTestSupport.NewAsync(fixture.Client);
        var tank = await Provisional.ProvisionalTestSupport.CreateTankAsync(fixture.Client, caller);
        using var added = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={FishWorld.Today}", caller.Token, new { name = "Bruno" });
        var fish = (await BodyAsync(added)).GetProperty("id").GetInt64();
        var photo = await _world.NewMediaAsync(caller.ProfileId);

        using var link = await SendAsync(fixture, HttpMethod.Post, FishWorld.Url(fish, "/media"), caller.Token, new { mediaItemId = photo });

        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Single((await _world.GetAsync(fish, caller.Token)).GetProperty("photos").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        await db.MediaItems.Where(m => m.Id == photo).ExecuteDeleteAsync();
    }
}
