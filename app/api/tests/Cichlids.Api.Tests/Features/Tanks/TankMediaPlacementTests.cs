using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// The media of a tank that is not public live under the private storage prefix and are reachable only
/// through signed paths; a change of the profile level moves them between the prefixes.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class TankMediaPlacementTests(UploadsFixture fixture)
{
    private static async Task<HttpResponseMessage> AddPhotoAsync(HttpClient client, string token, long tank, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task CopyObjectAsync(string source, string target)
    {
        await using var download = (await fixture.ObjectStore.GetAsync(source))!;
        using var buffer = new MemoryStream();
        await download.CopyToAsync(buffer);
        await fixture.ObjectStore.PutAsync(target, buffer, "image/jpeg");
    }

    private async Task<int> PlaceAllAsync()
    {
        var runs = 0;
        while (true)
        {
            using var scope = fixture.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<TankMediaPlacementService>().RunOnceAsync(CancellationToken.None)) return runs;
            runs++;
        }
    }

    private async Task<bool> PubliclyReadableAsync(string key)
    {
        using var anonymous = new HttpClient();
        using var response = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<MediaItem> ItemAsync(long mediaItemId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.MediaItems.Include(m => m.Variants).AsNoTracking().SingleAsync(m => m.Id == mediaItemId);
    }

    private async Task SetVisibilityAsync(string token, long tank, string level)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = level, stream = level });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenThePhotoIsAdded_ThenItIsStoredPrivatelyAndOnlyTheOwnerGetsSignedPaths()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client, isPublic: false);

        using var response = await AddPhotoAsync(fixture.Client, token, tank, PhotoFixtures.PhoneJpeg(1200, 800));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await BodyAsync(response);
        var mediaItemId = photo.GetProperty("mediaItemId").GetInt64();
        var thumb = photo.GetProperty("image").GetProperty("thumb").GetString()!;
        Assert.StartsWith("/api/tank-media/", thumb);
        var item = await ItemAsync(mediaItemId);
        Assert.StartsWith("private/tank/", item.StorageKey);
        Assert.All(item.Variants, v => Assert.StartsWith("private/variants/", v.StorageKey));
        foreach (var key in item.Variants.Select(v => v.StorageKey).Append(item.StorageKey))
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key));
            Assert.False(await PubliclyReadableAsync(key));
        }

        using var served = await fixture.Client.GetAsync(thumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", served.Headers.CacheControl?.ToString());

        using var ownSeries = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        Assert.StartsWith("/api/tank-media/", (await BodyAsync(ownSeries)).GetProperty("photos")[0].GetProperty("image").GetProperty("medium").GetString());
        using var foreignSeries = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", NewMemberToken());
        await AssertProblemAsync(foreignSeries, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymousSeries = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        await AssertProblemAsync(anonymousSeries, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Fact]
    public async Task GivenAPublicTank_WhenTheOwnerMakesItPrivate_ThenThePlacementMovesTheVariantsAndRemovesTheirPublicObjects()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var before = await ItemAsync(mediaItemId);
        var publicKeys = before.Variants.Select(v => v.StorageKey).ToList();
        Assert.All(publicKeys, key => Assert.StartsWith("variants/", key));
        foreach (var key in publicKeys) Assert.True(await PubliclyReadableAsync(key));

        await SetVisibilityAsync(token, tank, "private");
        Assert.Equal(1, await PlaceAllAsync());

        var after = await ItemAsync(mediaItemId);
        Assert.Equal(before.StorageKey, after.StorageKey);
        Assert.Equal(publicKeys.Select(k => "private/" + k).Order(), after.Variants.Select(v => v.StorageKey).Order());
        foreach (var key in publicKeys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key));
            Assert.False(await PubliclyReadableAsync(key));
            Assert.True(await fixture.ObjectStore.ExistsAsync("private/" + key));
        }

        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.False(stored.MediaMovePending);
        Assert.False(stored.MediaPlacedPublic);
        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        var large = (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image").GetProperty("medium").GetString()!;
        Assert.StartsWith("/api/tank-media/", large);
        using var served = await fixture.Client.GetAsync(large);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenTheOwnerMakesItPublic_ThenThePlacementMovesTheVariantsBackAndPublicUrlsWork()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client, isPublic: false);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var privateKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();

        await SetVisibilityAsync(token, tank, "public");
        await PlaceAllAsync();

        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.Equal(privateKeys.Select(k => k["private/".Length..]).Order(), publicKeys.Order());
        foreach (var key in publicKeys) Assert.True(await PubliclyReadableAsync(key));
        foreach (var key in privateKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        Assert.StartsWith("http", (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image").GetProperty("thumb").GetString());
    }

    [Fact]
    public async Task GivenAnInterruptedPlacement_WhenItRunsAgain_ThenItResumesAndLeavesNoPublicLeftover()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync();

        // An earlier pass died after the rows were repointed and before the public objects were deleted.
        foreach (var key in publicKeys)
        {
            await CopyObjectAsync("private/" + key, key);
        }

        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMovePending, true).SetProperty(t => t.MediaMoveNextAttemptAt, (DateTimeOffset?)null));
        }

        Assert.Equal(1, await PlaceAllAsync());
        foreach (var key in publicKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
        Assert.Equal(0, await PlaceAllAsync());
    }

    [Fact]
    public async Task GivenAPartiallyCopiedPlacement_WhenItRunsAgain_ThenTheMissingObjectsAreCopiedOnce()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        await CopyObjectAsync(publicKeys[0], "private/" + publicKeys[0]);

        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync();

        var keys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.Equal(publicKeys.Count, keys.Count);
        foreach (var key in keys) Assert.True(await fixture.ObjectStore.ExistsAsync(key));
        foreach (var key in publicKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
    }

    [Fact]
    public async Task GivenMediaThatAPostAlsoUses_WhenTheTankIsMadePrivate_ThenThatMediaStaysWhereItIs()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(800, 600));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        await using (var db = fixture.CreateDbContext())
        {
            var owner = (await db.MediaItems.SingleAsync(m => m.Id == mediaItemId)).OwnerProfileId!.Value;
            var post = new Post { AuthorProfileId = owner, State = PostState.Published, TankId = tank, Title = "Picture" };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForPost(post.Id, mediaItemId));
            await db.SaveChangesAsync();
        }

        var keys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).Order().ToList();
        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync();

        Assert.Equal(keys, (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).Order());
        await using var check = fixture.CreateDbContext();
        Assert.False((await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).MediaMovePending);
    }

    [Fact]
    public async Task GivenTwoWorkers_WhenOneTankIsDue_ThenOnlyOneClaimsIt()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        await SetVisibilityAsync(token, tank, "private");
        await using var firstDb = fixture.CreateDbContext();
        await using var secondDb = fixture.CreateDbContext();
        var options = Microsoft.Extensions.Options.Options.Create(new TankMediaPlacementOptions());
        var store = fixture.ObjectStore;
        var first = new TankMediaPlacementService(firstDb, store, TimeProvider.System, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<TankMediaPlacementService>.Instance);
        var second = new TankMediaPlacementService(secondDb, store, TimeProvider.System, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<TankMediaPlacementService>.Instance);

        var results = await Task.WhenAll(first.RunOnceAsync(CancellationToken.None), second.RunOnceAsync(CancellationToken.None));

        Assert.Equal(1, results.Count(ran => ran));
        Assert.False(await first.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GivenAFailingStore_WhenPlacing_ThenTheTankStaysPendingWithAGrowingDelay()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(800, 600));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var key = (await ItemAsync(mediaItemId)).Variants.First().StorageKey;
        await SetVisibilityAsync(token, tank, "private");
        await fixture.ObjectStore.DeleteAsync(key);

        await PlaceAllAsync();

        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.True(stored.MediaMovePending);
        Assert.Equal(1, stored.MediaMoveAttempts);
        Assert.True(stored.MediaMoveNextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenATamperedOrForeignToken_WhenRequestingTankMedia_ThenACodedNotFound()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client, isPublic: false);
        using var added = await AddPhotoAsync(fixture.Client, token, tank, Jpeg(800, 600));
        var thumb = (await BodyAsync(added)).GetProperty("image").GetProperty("thumb").GetString()!;
        var care = await LogAsync(fixture.Client, token, tank);

        using var tampered = await fixture.Client.GetAsync(thumb + "x");
        await AssertProblemAsync(tampered, HttpStatusCode.NotFound, "tank_media_not_found");
        using var careToken = await fixture.Client.GetAsync("/api/tank-media/" + fixture.Services.GetRequiredService<Cichlids.Api.Features.Care.CarePhotoUrlSigner>().PathFor(1)["/api/care-photos/".Length..]);
        await AssertProblemAsync(careToken, HttpStatusCode.NotFound, "tank_media_not_found");
        _ = care;
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenAVideoIsUploaded_ThenItIsTranscodedPrivatelyAndServedThroughSignedPaths()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client, isPublic: false);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await ProcessQueueAsync(fixture.Services);

        var item = await ItemAsync(mediaItemId);
        Assert.All(item.Variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        var video = Assert.Single((await BodyAsync(list)).EnumerateArray());
        var playlist = video.GetProperty("playlistUrl").GetString()!;
        Assert.StartsWith("/api/tank-videos/", playlist);
        using var master = await fixture.Client.GetAsync(playlist);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
        using var foreign = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", NewMemberToken());
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Fact]
    public async Task GivenAPublicTankVideo_WhenTheOwnerMakesTheTankPrivateAndPublicAgain_ThenTheFilesFollowAndStayPlayable()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await ProcessQueueAsync(fixture.Services);
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.True(await PubliclyReadableAsync(publicKeys.Single(k => k.EndsWith("master.m3u8"))));

        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync();

        var privateItem = await ItemAsync(mediaItemId);
        Assert.All(privateItem.Variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        foreach (var key in publicKeys) Assert.False(await PubliclyReadableAsync(key));
        var directory = publicKeys[0][..(publicKeys[0].LastIndexOf('/') + 1)];
        await foreach (var key in fixture.ObjectStore.ListKeysAsync(directory)) Assert.Fail($"A public object is left: {key}");
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        using var played = await fixture.Client.GetAsync((await BodyAsync(list))[0].GetProperty("playlistUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, played.StatusCode);

        await SetVisibilityAsync(token, tank, "public");
        await PlaceAllAsync();

        Assert.Equal(publicKeys.Order(), (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).Order());
        Assert.True(await PubliclyReadableAsync(publicKeys.Single(k => k.EndsWith("master.m3u8"))));
    }
}
