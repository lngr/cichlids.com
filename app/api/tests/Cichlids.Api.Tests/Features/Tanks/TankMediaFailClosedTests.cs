using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Storage;
using Cichlids.Tests.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// The media of a tank that is not public are never handed out by public URL and leave the public prefix
/// even when the placement fails midway, is retried, or races with an upload or a transcode.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class TankMediaFailClosedTests(UploadsFixture fixture)
{
    private sealed class Host : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required FlakyObjectStore Store { get; init; }
        public HttpClient Client { get; init; } = null!;
        public ValueTask DisposeAsync() { Client.Dispose(); return Factory.DisposeAsync(); }
    }

    private Host NewHost(string immediateBudget = "00:00:00")
    {
        var store = new FlakyObjectStore(fixture.ObjectStore);
        var factory = fixture.CreateHost(
            new Dictionary<string, string?> { ["TankMediaPlacement:ImmediateBudget"] = immediateBudget },
            services => services.AddSingleton<IObjectStore>(store));
        return new Host { Factory = factory, Store = store, Client = factory.CreateClient() };
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

    private static async Task<int> PlaceAllAsync(IServiceProvider services)
    {
        var runs = 0;
        while (true)
        {
            using var scope = services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<TankMediaPlacementService>().RunOnceAsync(CancellationToken.None)) return runs;
            runs++;
        }
    }

    private async Task SetVisibilityAsync(HttpClient client, string token, long tank, string level)
    {
        using var response = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = level, stream = level });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<string>> VariantKeysAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        var ids = db.MediaLinks.Where(l => l.TankId == tank).Select(l => l.MediaItemId);
        return await db.MediaVariants.Where(v => ids.Contains(v.MediaItemId)).Select(v => v.StorageKey).ToListAsync();
    }

    private async Task<bool> PubliclyReadableAsync(string key)
    {
        using var anonymous = new HttpClient();
        using var response = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task AssertNothingPublicAsync(long tank)
    {
        var keys = await VariantKeysAsync(tank);
        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.StartsWith("private/", key));
        foreach (var key in keys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key["private/".Length..]), $"A public object is left: {key}");
            Assert.False(await PubliclyReadableAsync(key["private/".Length..]));
        }
    }

    [Fact]
    public async Task GivenATankThatTurnedPrivateWhileItsObjectsStillLieUnderThePublicPrefix_ThenNoResponseHandsOutAPublicUrl()
    {
        await using var host = NewHost();
        var (token, tank) = await OwnTankAsync(host.Client);
        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));
        Assert.StartsWith("http", (await BodyAsync(added)).GetProperty("image").GetProperty("thumb").GetString());
        await SetVisibilityAsync(host.Client, token, tank, "private");
        Assert.All(await VariantKeysAsync(tank), key => Assert.StartsWith("variants/", key));

        using var series = await SendAsync(host.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        var image = (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image");
        foreach (var label in new[] { "thumb", "small", "medium" })
        {
            Assert.StartsWith("/api/tank-media/", image.GetProperty(label).GetString());
        }

        using var detail = await SendAsync(host.Client, HttpMethod.Get, $"/api/tanks/{tank}", token);
        Assert.Equal("private", (await BodyAsync(detail)).GetProperty("visibility").GetProperty("profile").GetString());
        using var foreign = await SendAsync(host.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", NewMemberToken());
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "tank_not_found");
        using var served = await host.Client.GetAsync(image.GetProperty("thumb").GetString());
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
    }

    [Fact]
    public async Task GivenAStoreThatFailsMidway_WhenThePlacementRuns_ThenItStaysPendingServesSignedUrlsAndRetriesUntilNothingIsPublic()
    {
        await using var host = NewHost();
        var (token, tank) = await OwnTankAsync(host.Client);
        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        await SetVisibilityAsync(host.Client, token, tank, "private");
        host.Store.RefusePutsUnder = "private/variants/";
        host.Store.AllowedPutsBeforeRefusal = 1;

        await PlaceAllAsync(host.Factory.Services);

        await using (var db = fixture.CreateDbContext())
        {
            var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
            Assert.True(stored.MediaMovePending);
            Assert.Equal(1, stored.MediaMoveAttempts);
            Assert.True(stored.MediaMoveNextAttemptAt > DateTimeOffset.UtcNow);
        }

        var midway = await VariantKeysAsync(tank);
        Assert.Contains(midway, key => key.StartsWith("private/", StringComparison.Ordinal));
        Assert.Contains(midway, key => key.StartsWith("variants/", StringComparison.Ordinal));
        using (var series = await SendAsync(host.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token))
        {
            var image = (await BodyAsync(series)).GetProperty("photos")[0].GetProperty("image");
            foreach (var label in new[] { "thumb", "small", "medium" })
            {
                Assert.StartsWith("/api/tank-media/", image.GetProperty(label).GetString());
            }
        }

        host.Store.RefusePutsUnder = null;
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMoveNextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        Assert.Equal(1, await PlaceAllAsync(host.Factory.Services));
        await AssertNothingPublicAsync(tank);
        await using var check = fixture.CreateDbContext();
        var done = await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.False(done.MediaMovePending);
        Assert.False(done.MediaPlacedPublic);
        Assert.Equal(0, done.MediaMoveAttempts);
    }

    [Fact]
    public async Task GivenADeleteThatSilentlyDoesNothing_WhenThePlacementRuns_ThenTheTankIsNeverConsideredDone()
    {
        await using var host = NewHost();
        var (token, tank) = await OwnTankAsync(host.Client);
        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        await SetVisibilityAsync(host.Client, token, tank, "private");
        host.Store.IgnoreDeletes = true;

        await PlaceAllAsync(host.Factory.Services);

        await using (var db = fixture.CreateDbContext())
        {
            var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
            Assert.True(stored.MediaMovePending);
            Assert.True(stored.MediaMoveAttempts >= 1);
        }

        host.Store.IgnoreDeletes = false;
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMoveNextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        await PlaceAllAsync(host.Factory.Services);
        await AssertNothingPublicAsync(tank);
    }

    [Fact]
    public async Task GivenTheImmediateBudget_WhenTheOwnerMakesTheTankPrivate_ThenTheObjectsAreGoneFromThePublicPrefixBeforeTheAnswer()
    {
        await using var host = NewHost("00:00:30");
        var (token, tank) = await OwnTankAsync(host.Client);
        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        await SetVisibilityAsync(host.Client, token, tank, "private");

        await AssertNothingPublicAsync(tank);
        await using var db = fixture.CreateDbContext();
        Assert.False((await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).MediaMovePending);
    }

    [Fact]
    public async Task GivenAFailingStore_WhenTheOwnerMakesTheTankPrivate_ThenTheAnswerIsStillOkAndTheWorkerCompletesLater()
    {
        await using var host = NewHost("00:00:30");
        var (token, tank) = await OwnTankAsync(host.Client);
        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        host.Store.RefusePutsUnder = "private/variants/";

        await SetVisibilityAsync(host.Client, token, tank, "private");

        await using (var db = fixture.CreateDbContext())
        {
            Assert.True((await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).MediaMovePending);
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.MediaMoveNextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        host.Store.RefusePutsUnder = null;
        await PlaceAllAsync(host.Factory.Services);
        await AssertNothingPublicAsync(tank);
    }

    [Fact]
    public async Task GivenAVisibilityChangeDuringASeriesPhotoUpload_WhenThePlacementRuns_ThenNoPublicObjectOfThePrivateTankRemains()
    {
        await using var host = NewHost();
        var (token, tank) = await OwnTankAsync(host.Client);
        var flipped = false;
        host.Store.BeforePut = async key =>
        {
            if (flipped || !key.StartsWith("variants/", StringComparison.Ordinal)) return;
            flipped = true;
            // The upload chose the public prefix; the owner makes the tank private before the photo commits.
            await SetVisibilityAsync(host.Client, token, tank, "private");
            await PlaceAllAsync(host.Factory.Services);
        };

        using var added = await AddPhotoAsync(host.Client, token, tank, Jpeg(1200, 800));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.True(flipped);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.True((await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).MediaMovePending,
                "The commit of the photo notices the changed level and queues the placement.");
        }

        host.Store.BeforePut = null;
        await PlaceAllAsync(host.Factory.Services);
        await AssertNothingPublicAsync(tank);
        var body = await BodyAsync(added);
        Assert.StartsWith("/api/tank-media/", body.GetProperty("image").GetProperty("thumb").GetString());
    }

    [Fact]
    public async Task GivenAVisibilityChangeDuringATranscode_WhenThePlacementRuns_ThenNoPublicObjectOfThePrivateTankRemains()
    {
        await using var host = NewHost();
        var (token, tank) = await OwnTankAsync(host.Client);
        var created = await UploadedAsync(host.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        var flipped = false;
        host.Store.BeforePut = async key =>
        {
            if (flipped || !key.StartsWith("variants/video/", StringComparison.Ordinal)) return;
            flipped = true;
            await SetVisibilityAsync(host.Client, token, tank, "private");
            await PlaceAllAsync(host.Factory.Services);
        };

        await ProcessQueueAsync(host.Factory.Services);

        Assert.True(flipped);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.True((await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).MediaMovePending);
        }

        host.Store.BeforePut = null;
        await PlaceAllAsync(host.Factory.Services);

        await using var check = fixture.CreateDbContext();
        var variants = await check.MediaVariants.Where(v => v.MediaItemId == mediaItemId).ToListAsync();
        Assert.NotEmpty(variants);
        Assert.All(variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        var publicDirectory = variants[0].StorageKey["private/".Length..];
        publicDirectory = publicDirectory[..(publicDirectory.LastIndexOf('/') + 1)];
        await foreach (var key in fixture.ObjectStore.ListKeysAsync(publicDirectory)) Assert.Fail($"A public object is left: {key}");
    }
}
