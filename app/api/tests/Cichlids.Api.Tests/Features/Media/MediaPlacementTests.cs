using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Media;

/// <summary>
/// The media of a tank that is not public live under the private storage prefix and are reachable only
/// through signed paths; a change of the visibility moves the variants of each media item between the
/// prefixes, and a media item that a published story also shows stays in the public area.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class MediaPlacementTests(UploadsFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient client = null!;
    private IServiceProvider services = null!;

    public Task InitializeAsync()
    {
        // The tests run the placement directly, so a change of the visibility does not wait for it.
        _host = fixture.CreateHost(new Dictionary<string, string?> { ["MediaPlacement:ImmediateBudget"] = "00:00:00" }, null);
        client = _host.CreateClient();
        services = _host.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await _host.DisposeAsync();
    }

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

    private async Task<List<long>> ItemIdsOfAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        return await db.MediaLinks.Where(l => l.TankId == tank).Select(l => l.MediaItemId).ToListAsync();
    }

    // Places the due media items of the tank and returns how many were placed.
    private async Task<int> PlaceAllAsync(long tank)
    {
        var ids = await ItemIdsOfAsync(tank);
        var runs = 0;
        while (true)
        {
            using var scope = services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<MediaPlacementService>().RunOnceAsync(CancellationToken.None, ids)) return runs;
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
        using var response = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = level, stream = level });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenThePhotoIsAdded_ThenItIsStoredPrivatelyAndOnlyTheOwnerGetsSignedPaths()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);

        using var response = await AddPhotoAsync(client, token, tank, PhotoFixtures.PhoneJpeg(1200, 800));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await BodyAsync(response);
        var mediaItemId = photo.GetProperty("mediaItemId").GetInt64();
        var thumb = photo.GetProperty("image").GetProperty("thumb").GetString()!;
        Assert.StartsWith("/api/media/photos/", thumb);
        var item = await ItemAsync(mediaItemId);
        Assert.StartsWith("private/tank/", item.StorageKey);
        Assert.All(item.Variants, v => Assert.StartsWith("private/variants/", v.StorageKey));
        foreach (var key in item.Variants.Select(v => v.StorageKey).Append(item.StorageKey))
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key));
            Assert.False(await PubliclyReadableAsync(key));
        }

        using var served = await client.GetAsync(thumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", served.Headers.CacheControl?.ToString());

        using var ownSeries = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        Assert.StartsWith("/api/media/photos/", (await BodyAsync(ownSeries)).GetProperty("photos")[0].GetProperty("image").GetProperty("medium").GetString());
        using var foreignSeries = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", NewMemberToken());
        await AssertProblemAsync(foreignSeries, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymousSeries = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        await AssertProblemAsync(anonymousSeries, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Fact]
    public async Task GivenAPublicTank_WhenTheOwnerMakesItPrivate_ThenThePlacementMovesTheVariantsAndRemovesTheirPublicObjects()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var before = await ItemAsync(mediaItemId);
        var publicKeys = before.Variants.Select(v => v.StorageKey).ToList();
        Assert.All(publicKeys, key => Assert.StartsWith("variants/", key));
        foreach (var key in publicKeys) Assert.True(await PubliclyReadableAsync(key));

        await SetVisibilityAsync(token, tank, "private");
        Assert.Equal(1, await PlaceAllAsync(tank));

        var after = await ItemAsync(mediaItemId);
        Assert.Equal(before.StorageKey, after.StorageKey);
        Assert.Equal(publicKeys.Select(k => "private/" + k).Order(), after.Variants.Select(v => v.StorageKey).Order());
        foreach (var key in publicKeys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key));
            Assert.False(await PubliclyReadableAsync(key));
            Assert.True(await fixture.ObjectStore.ExistsAsync("private/" + key));
        }

        Assert.False(after.PlacementPending);
        Assert.False(after.PlacedPublic);
        using var series = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        var large = (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image").GetProperty("medium").GetString()!;
        Assert.StartsWith("/api/media/photos/", large);
        using var served = await client.GetAsync(large);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenTheOwnerMakesItPublic_ThenThePlacementMovesTheVariantsBackAndPublicUrlsWork()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var privateKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();

        await SetVisibilityAsync(token, tank, "public");
        await PlaceAllAsync(tank);

        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.Equal(privateKeys.Select(k => k["private/".Length..]).Order(), publicKeys.Order());
        foreach (var key in publicKeys) Assert.True(await PubliclyReadableAsync(key));
        foreach (var key in privateKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
        using var series = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        Assert.StartsWith("http", (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image").GetProperty("thumb").GetString());
    }

    [Fact]
    public async Task GivenAnInterruptedPlacement_WhenItRunsAgain_ThenItResumesAndLeavesNoPublicLeftover()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync(tank);

        // An earlier pass died after the rows were repointed and before the public objects were deleted.
        foreach (var key in publicKeys)
        {
            await CopyObjectAsync("private/" + key, key);
        }

        await using (var db = fixture.CreateDbContext())
        {
            await db.MediaItems.Where(m => m.Id == mediaItemId).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.PlacementPending, true).SetProperty(m => m.PlacementNextAttemptAt, (DateTimeOffset?)null));
        }

        Assert.Equal(1, await PlaceAllAsync(tank));
        foreach (var key in publicKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
        Assert.Equal(0, await PlaceAllAsync(tank));
    }

    [Fact]
    public async Task GivenAPartiallyCopiedPlacement_WhenItRunsAgain_ThenTheMissingObjectsAreCopiedOnce()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(1200, 800));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        await CopyObjectAsync(publicKeys[0], "private/" + publicKeys[0]);

        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync(tank);

        var keys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.Equal(publicKeys.Count, keys.Count);
        foreach (var key in keys) Assert.True(await fixture.ObjectStore.ExistsAsync(key));
        foreach (var key in publicKeys) Assert.False(await fixture.ObjectStore.ExistsAsync(key));
    }

    [Fact]
    public async Task GivenMediaThatAPublishedStoryAlsoShows_WhenTheTankIsMadePrivate_ThenThatMediaStaysInThePublicArea()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(800, 600));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        await using (var db = fixture.CreateDbContext())
        {
            var owner = (await db.MediaItems.SingleAsync(m => m.Id == mediaItemId)).OwnerProfileId!.Value;
            var story = new Story { AuthorProfileId = owner, State = StoryState.Published, Subjects = [StorySubject.ForTank(tank)], Title = "Picture" };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForStory(story.Id, mediaItemId));
            await db.SaveChangesAsync();
        }

        var keys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).Order().ToList();
        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync(tank);

        var item = await ItemAsync(mediaItemId);
        Assert.Equal(keys, item.Variants.Select(v => v.StorageKey).Order());
        Assert.All(keys, key => Assert.StartsWith("variants/", key));
        Assert.False(item.PlacementPending);
        Assert.True(item.PlacedPublic);
        foreach (var key in keys) Assert.True(await PubliclyReadableAsync(key));
    }

    [Fact]
    public async Task GivenTwoWorkers_WhenOneMediaItemIsDue_ThenOnlyOneClaimsIt()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(800, 600));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        await SetVisibilityAsync(token, tank, "private");
        var ids = await ItemIdsOfAsync(tank);
        await using var firstDb = fixture.CreateDbContext();
        await using var secondDb = fixture.CreateDbContext();
        var options = Microsoft.Extensions.Options.Options.Create(new MediaPlacementOptions());
        var store = fixture.ObjectStore;
        var first = new MediaPlacementService(firstDb, store, TimeProvider.System, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaPlacementService>.Instance);
        var second = new MediaPlacementService(secondDb, store, TimeProvider.System, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaPlacementService>.Instance);

        var results = await Task.WhenAll(first.RunOnceAsync(CancellationToken.None, ids), second.RunOnceAsync(CancellationToken.None, ids));

        Assert.Equal(1, results.Count(ran => ran));
        Assert.False(await first.RunOnceAsync(CancellationToken.None, ids));
    }

    [Fact]
    public async Task GivenAFailingStore_WhenPlacing_ThenTheMediaItemStaysPendingWithAGrowingDelay()
    {
        var (token, tank) = await OwnTankAsync(client);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(800, 600));
        var mediaItemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var key = (await ItemAsync(mediaItemId)).Variants.First().StorageKey;
        await SetVisibilityAsync(token, tank, "private");
        await fixture.ObjectStore.DeleteAsync(key);

        await PlaceAllAsync(tank);

        var stored = await ItemAsync(mediaItemId);
        Assert.True(stored.PlacementPending);
        Assert.Equal(1, stored.PlacementAttempts);
        Assert.True(stored.PlacementNextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GivenATamperedOrForeignToken_WhenRequestingMedia_ThenACodedNotFound()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        using var added = await AddPhotoAsync(client, token, tank, Jpeg(800, 600));
        var thumb = (await BodyAsync(added)).GetProperty("image").GetProperty("thumb").GetString()!;

        using var tampered = await client.GetAsync(thumb + "x");
        await AssertProblemAsync(tampered, HttpStatusCode.NotFound, "media_not_found");
        var videoToken = services.GetRequiredService<MediaUrlSigner>().VideoPathFor(1, 0, "master.m3u8")[MediaUrlSigner.VideoRoute.Length..].Split('/')[0];
        using var wrongKind = await client.GetAsync(MediaUrlSigner.PhotoRoute + videoToken);
        await AssertProblemAsync(wrongKind, HttpStatusCode.NotFound, "media_not_found");
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenAVideoIsUploaded_ThenItIsTranscodedPrivatelyAndServedThroughSignedPaths()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        var created = await UploadedAsync(client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await ProcessQueueAsync(services);

        var item = await ItemAsync(mediaItemId);
        Assert.All(item.Variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        Assert.False(item.PlacedPublic);
        using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        var video = Assert.Single((await BodyAsync(list)).EnumerateArray());
        var playlist = video.GetProperty("playlistUrl").GetString()!;
        Assert.StartsWith("/api/media/videos/", playlist);
        using var master = await client.GetAsync(playlist);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
        using var foreign = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/videos", NewMemberToken());
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Fact]
    public async Task GivenAPublicTankVideo_WhenTheOwnerMakesTheTankPrivateAndPublicAgain_ThenTheFilesFollowAndStayPlayable()
    {
        var (token, tank) = await OwnTankAsync(client);
        var created = await UploadedAsync(client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await ProcessQueueAsync(services);
        var publicKeys = (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).ToList();
        Assert.All(publicKeys, key => Assert.StartsWith("variants/video/", key));
        Assert.True(await PubliclyReadableAsync(publicKeys.Single(k => k.EndsWith("master.m3u8"))));

        await SetVisibilityAsync(token, tank, "private");
        await PlaceAllAsync(tank);

        var privateItem = await ItemAsync(mediaItemId);
        Assert.All(privateItem.Variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        foreach (var key in publicKeys) Assert.False(await PubliclyReadableAsync(key));
        var directory = publicKeys[0][..(publicKeys[0].LastIndexOf('/') + 1)];
        await foreach (var key in fixture.ObjectStore.ListKeysAsync(directory)) Assert.Fail($"A public object is left: {key}");
        using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        using var played = await client.GetAsync((await BodyAsync(list))[0].GetProperty("playlistUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, played.StatusCode);

        await SetVisibilityAsync(token, tank, "public");
        await PlaceAllAsync(tank);

        Assert.Equal(publicKeys.Order(), (await ItemAsync(mediaItemId)).Variants.Select(v => v.StorageKey).Order());
        Assert.True(await PubliclyReadableAsync(publicKeys.Single(k => k.EndsWith("master.m3u8"))));
    }
}
