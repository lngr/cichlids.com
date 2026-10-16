using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Decommissioning;

/// <summary>
/// A member decommissions a tank with a photo of the empty tank from the camera of the app and puts it
/// back into operation with a photo of the filled tank. The photos are checked for their provenance and
/// stored as tank media; the decommissionings are visible to the viewers of the care stream.
/// </summary>
[Collection(UploadsCollection.Name)]
[Trait("Story", "task-6")]
public sealed class TankDecommissioningTests(UploadsFixture fixture)
{
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static string Url(long tank, string suffix = "", string? date = null) =>
        $"/api/tanks/{tank}/decommissioning{suffix}?date={date ?? Today}&utcOffsetMinutes=0";

    private static string NowExif(TimeSpan shift) => (DateTime.UtcNow + shift).ToString("yyyy:MM:dd HH:mm:ss");

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? token, string url, byte[]? photo, string? capturedInApp = "true",
        string? scope = null, string contentType = "image/jpeg")
    {
        using var content = new MultipartFormDataContent();
        if (capturedInApp is not null) content.Add(new StringContent(capturedInApp), "capturedInApp");
        if (scope is not null) content.Add(new StringContent(scope), "scope");
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(file, "file", "photo.jpg");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, string? token, long tank, byte[]? photo, string scope = "all_care",
        string? capturedInApp = "true", string? date = null) =>
        PostAsync(client, token, Url(tank, date: date), photo, capturedInApp, scope);

    private static Task<HttpResponseMessage> EndAsync(HttpClient client, string? token, long tank, byte[]? photo, string? capturedInApp = "true") =>
        PostAsync(client, token, Url(tank, "/end"), photo, capturedInApp);

    [Fact]
    public async Task GivenAnAppPhotoOfTheEmptyTank_WhenDecommissioning_ThenTheDecommissioningIsActiveAndThePhotoIsLinkedToTheTankInTheDecommissionRole()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await StartAsync(fixture.Client, token, tank, Jpeg(640, 480), "water_change");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.True(body.GetProperty("decommissioned").GetBoolean());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("water_change", item.GetProperty("scope").GetString());
        Assert.True(item.GetProperty("active").GetBoolean());
        Assert.Equal(Today, item.GetProperty("startDate").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("endDate").ValueKind);
        Assert.StartsWith("http", item.GetProperty("startImage").GetProperty("thumb").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("endImage").ValueKind);

        await using var db = fixture.CreateDbContext();
        var row = await db.TankDecommissionings.SingleAsync(d => d.TankId == tank);
        Assert.Equal(DecommissionScope.WaterChange, row.Scope);
        Assert.Null(row.EndedAt);
        var media = await db.MediaLinks.SingleAsync(l => l.TankId == tank);
        Assert.Equal(MediaLinkRole.Decommission, media.Role);
        Assert.True((await db.MediaItems.SingleAsync(i => i.Id == media.MediaItemId)).CapturedInApp);
        Assert.Equal(media.MediaItemId, row.StartMediaItemId);
        var recorded = Assert.Single(await EventsAsync(db, "tank", tank), e => e.Type == "tank.decommissioned");
        Assert.Equal(tank, recorded.Payload.GetProperty("tankId").GetInt64());
        Assert.Equal("water_change", recorded.Payload.GetProperty("scope").GetString());
        Assert.Equal(row.StartMediaItemId, recorded.Payload.GetProperty("mediaItemId").GetInt64());
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public async Task GivenAPhotoNotTakenWithTheAppCamera_WhenDecommissioning_ThenItIsRefusedAndNothingIsStored(string? capturedInApp)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await StartAsync(fixture.Client, token, tank, Jpeg(641, 480), capturedInApp: capturedInApp);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "decommission_photo_not_in_app");

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.TankDecommissionings.AnyAsync(d => d.TankId == tank));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.TankId == tank));
        Assert.DoesNotContain(await EventsAsync(db, "tank", tank), e => e.Type == "tank.decommissioned");
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-2000)]
    [InlineData(30)]
    public async Task GivenACaptureTimeFarFromNow_WhenDecommissioning_ThenItIsRefused(int minutes)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var photo = PhotoFixtures.JpegWithCaptureTime(NowExif(TimeSpan.FromMinutes(minutes)), "+00:00");
        using var response = await StartAsync(fixture.Client, token, tank, photo);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "decommission_photo_time");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.TankDecommissionings.AnyAsync(d => d.TankId == tank));
    }

    [Fact]
    public async Task GivenACaptureTimeJustBeforeNow_WhenDecommissioning_ThenItIsAccepted()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var photo = PhotoFixtures.JpegWithCaptureTime(NowExif(TimeSpan.FromMinutes(-2)), "+00:00");
        using var response = await StartAsync(fixture.Client, token, tank, photo);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GivenAPhotoThatAlreadyProvedADecommissioning_WhenUsingItAgain_ThenItIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var photo = Jpeg(650, 480);
        using var first = await StartAsync(fixture.Client, token, tank, photo);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var end = await EndAsync(fixture.Client, token, tank, photo);
        await AssertProblemAsync(end, HttpStatusCode.BadRequest, "decommission_photo_reused");
        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.TankDecommissionings.SingleAsync(d => d.TankId == tank)).EndedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("everything")]
    public async Task GivenNoOrAnUnknownScope_WhenDecommissioning_ThenItIsRefused(string? scope)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await PostAsync(fixture.Client, token, Url(tank), Jpeg(651, 480), scope: scope);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "decommission_scope_invalid");
    }

    [Fact]
    public async Task GivenNoDecodablePhoto_WhenDecommissioning_ThenItIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var missing = await StartAsync(fixture.Client, token, tank, null);
        await AssertProblemAsync(missing, HttpStatusCode.BadRequest, "decommission_photo_invalid");
        using var text = await PostAsync(fixture.Client, token, Url(tank), "hello"u8.ToArray(), scope: "all_care", contentType: "text/plain");
        await AssertProblemAsync(text, HttpStatusCode.BadRequest, "decommission_photo_invalid");
        using var broken = await StartAsync(fixture.Client, token, tank, UndecodableJpeg());
        await AssertProblemAsync(broken, HttpStatusCode.BadRequest, "decommission_photo_invalid");
    }

    [Fact]
    public async Task GivenAnActiveDecommissioning_WhenDecommissioningAgain_ThenItIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var first = await StartAsync(fixture.Client, token, tank, Jpeg(652, 480));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await StartAsync(fixture.Client, token, tank, Jpeg(653, 480));
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "decommission_active");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.MediaLinks.CountAsync(l => l.TankId == tank));
    }

    [Fact]
    public async Task GivenOtherMemberUnknownTankOrAnonymous_WhenDecommissioning_ThenCodedRefusals()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        using var foreign = await StartAsync(fixture.Client, NewMemberToken(), tank, Jpeg(654, 480));
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "tank_not_found");
        using var unknown = await StartAsync(fixture.Client, owner, long.MaxValue, Jpeg(655, 480));
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymous = await StartAsync(fixture.Client, null, tank, Jpeg(656, 480));
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "decommission_auth_required");
        using var badDate = await StartAsync(fixture.Client, owner, tank, Jpeg(657, 480), date: "yesterday");
        await AssertProblemAsync(badDate, HttpStatusCode.BadRequest, "decommission_date_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.TankDecommissionings.AnyAsync(d => d.TankId == tank));
    }

    [Fact]
    public async Task GivenAnAppPhotoOfTheFilledTank_WhenPuttingTheTankBackIntoOperation_ThenTheDecommissioningEndsToday()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var started = await StartAsync(fixture.Client, token, tank, Jpeg(660, 480));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var ended = await EndAsync(fixture.Client, token, tank, Jpeg(661, 480));
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        var body = await BodyAsync(ended);
        Assert.False(body.GetProperty("decommissioned").GetBoolean());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("active").GetBoolean());
        Assert.Equal(Today, item.GetProperty("endDate").GetString());
        Assert.StartsWith("http", item.GetProperty("endImage").GetProperty("thumb").GetString());

        await using var db = fixture.CreateDbContext();
        var row = await db.TankDecommissionings.SingleAsync(d => d.TankId == tank);
        Assert.NotNull(row.EndedAt);
        var media = await db.MediaLinks.Where(l => l.TankId == tank).OrderBy(l => l.Id).ToListAsync();
        Assert.Equal([MediaLinkRole.Decommission, MediaLinkRole.Recommission], media.Select(l => l.Role));
        Assert.Equal(2, await db.MediaItems.CountAsync(i => media.Select(m => m.MediaItemId).Contains(i.Id) && i.CapturedInApp));
        Assert.Equal(media[1].MediaItemId, row.EndMediaItemId);
        var ending = Assert.Single(await EventsAsync(db, "tank", tank), e => e.Type == "tank.recommissioned");
        Assert.Equal(media[1].MediaItemId, ending.Payload.GetProperty("mediaItemId").GetInt64());
    }

    [Fact]
    public async Task GivenNoActiveDecommissioning_WhenPuttingTheTankBackIntoOperation_ThenItIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await EndAsync(fixture.Client, token, tank, Jpeg(662, 480));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "decommission_not_active");
    }

    [Fact]
    public async Task GivenAPhotoNotTakenWithTheAppCamera_WhenPuttingTheTankBackIntoOperation_ThenTheTankStaysDecommissioned()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var started = await StartAsync(fixture.Client, token, tank, Jpeg(663, 480));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var response = await EndAsync(fixture.Client, token, tank, Jpeg(664, 480), capturedInApp: "false");
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "decommission_photo_not_in_app");
        await using var db = fixture.CreateDbContext();
        Assert.Null((await db.TankDecommissionings.SingleAsync(d => d.TankId == tank)).EndedAt);
        Assert.Equal(1, await db.MediaLinks.CountAsync(l => l.TankId == tank));
    }

    [Fact]
    public async Task GivenSeveralDecommissionings_WhenReadingTheTank_ThenTheHistoryKeepsEveryPairNewestFirst()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        for (var i = 0; i < 2; i++)
        {
            using var started = await StartAsync(fixture.Client, token, tank, Jpeg(670 + i, 480), i == 0 ? "all_care" : "water_change");
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            using var ended = await EndAsync(fixture.Client, token, tank, Jpeg(680 + i, 480));
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        }
        using var read = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), token);
        var items = (await BodyAsync(read)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["water_change", "all_care"], items.Select(i => i.GetProperty("scope").GetString()));
        Assert.All(items, i => Assert.False(i.GetProperty("active").GetBoolean()));
    }

    [Fact]
    public async Task GivenARunningTankWithADecommissionedMonth_WhenAVisitorOpensTheTank_ThenTheOperatingTimeLeavesOutTheDecommissionedDays()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = today.AddDays(-245);
        var (first, end) = (today.AddDays(-100), today.AddDays(-70));
        await using (var db = fixture.CreateDbContext())
        {
            var owner = await db.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync();
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, new DateTimeOffset(created.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
            var photo = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = owner, CreatedAt = DateTimeOffset.UtcNow };
            db.MediaItems.Add(photo);
            await db.SaveChangesAsync();
            db.TankDecommissionings.Add(new TankDecommissioning
            {
                TankId = tank, Scope = DecommissionScope.AllCare, StartedAt = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                StartDate = first, StartMediaItemId = photo.Id, EndedAt = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                EndDate = end, EndMediaItemId = photo.Id,
            });
            await db.SaveChangesAsync();
        }

        using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), null);
        Assert.Equal(HttpStatusCode.OK, visitor.StatusCode);
        var body = await BodyAsync(visitor);
        Assert.Equal(215, body.GetProperty("operatingDays").GetInt32());
        Assert.Equal(30, body.GetProperty("decommissionedDays").GetInt32());
        Assert.Equal(Decommissions.OperationOn(created, [new DecommissionSpan(first, end)], today).OperatingMonths, body.GetProperty("operatingMonths").GetInt32());
        Assert.False(body.GetProperty("decommissioned").GetBoolean());
    }

    [Fact]
    public async Task GivenATankWhoseStreamIsPrivate_WhenAVisitorOpensTheDecommissioning_ThenItIsUnavailableToThemButVisibleToTheOwner()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var started = await StartAsync(fixture.Client, token, tank, Jpeg(690, 480));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var visible = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), null);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.True((await BodyAsync(visible)).GetProperty("decommissioned").GetBoolean());

        using var closed = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = "public", stream = "private" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var hidden = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), NewMemberToken());
        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymous = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), null);
        await AssertProblemAsync(anonymous, HttpStatusCode.NotFound, "tank_not_found");
        using var owner = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), token);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task GivenTheProofPhotos_WhenOpeningTheTimeLapseTheTankPageOrTheList_ThenTheyAreNotPartOfTheTankGallery()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var started = await StartAsync(fixture.Client, token, tank, Jpeg(692, 480));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var ended = await EndAsync(fixture.Client, token, tank, Jpeg(693, 480));
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);

        using var series = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank}/time-lapse"));
        Assert.Equal(0, series.RootElement.GetProperty("photos").GetArrayLength());
        using var detail = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank}"));
        Assert.All(new[] { "showcase", "decoration", "technic" }, section => Assert.Equal(0, detail.RootElement.GetProperty("sections").GetProperty(section).GetArrayLength()));
        using var list = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks?userId={await OwnerAsync(tank)}"));
        Assert.Equal(0, list.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt64() == tank).GetProperty("imageCount").GetInt32());
    }

    private async Task<long> OwnerAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync();
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenAVisitorOpensTheDecommissioning_ThenItIsUnavailable()
    {
        var token = NewMemberToken();
        var tank = await CreateTankAsync(fixture.Client, token, isPublic: false);
        using var started = await StartAsync(fixture.Client, token, tank, Jpeg(691, 480));
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, Url(tank), null);
        await AssertProblemAsync(visitor, HttpStatusCode.NotFound, "tank_not_found");
    }
}
