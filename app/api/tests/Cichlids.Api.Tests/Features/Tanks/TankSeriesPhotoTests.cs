// Story: TASK-5.11
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// Adding a photo straight to the time-lapse series of an own tank.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class TankSeriesPhotoTests(UploadsFixture fixture)
{
    [Fact]
    public async Task GivenOwnTank_WhenAddingAPhoto_ThenItIsPublicTankMediaWithoutAPostAndJoinsTheSeries()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await AddAsync(fixture.Client, token, tank, PhotoFixtures.PhoneJpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await BodyAsync(response);
        var mediaItemId = photo.GetProperty("mediaItemId").GetInt64();
        Assert.StartsWith("http", photo.GetProperty("image").GetProperty("thumb").GetString());
        Assert.Contains("variants/", photo.GetProperty("image").GetProperty("thumb").GetString());
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 10, 20, 30, TimeSpan.FromHours(2)), photo.GetProperty("takenAt").GetDateTimeOffset());

        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == mediaItemId);
        Assert.StartsWith("originals/uploads/", item.StorageKey);
        Assert.Equal(3, item.Variants.Count);
        Assert.All(item.Variants, v => Assert.StartsWith("variants/", v.StorageKey));
        Assert.True(await fixture.ObjectStore.ExistsAsync(item.StorageKey));
        var media = await db.MediaLinks.SingleAsync(l => l.TankId == tank);
        Assert.Equal(mediaItemId, media.MediaItemId);
        Assert.Equal(MediaLinkRole.TimeLapse, media.Role);
        Assert.False(await db.Posts.AnyAsync(p => p.AuthorProfileId == item.OwnerProfileId));

        using var series = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank}/time-lapse"));
        var listed = Assert.Single(series.RootElement.GetProperty("photos").EnumerateArray());
        Assert.Equal(mediaItemId, listed.GetProperty("mediaItemId").GetInt64());
    }

    [Fact]
    public async Task GivenTwoPhotos_WhenTheLaterCaptureIsAddedFirst_ThenTheSeriesIsOrderedByCaptureTime()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var later = await AddAsync(fixture.Client, token, tank, PhotoFixtures.JpegWithCaptureTime("2025:03:01 09:00:00", "+01:00"));
        using var earlier = await AddAsync(fixture.Client, token, tank, PhotoFixtures.JpegWithCaptureTime("2023:03:01 09:00:00", "+01:00"));
        var laterId = (await BodyAsync(later)).GetProperty("mediaItemId").GetInt64();
        var earlierId = (await BodyAsync(earlier)).GetProperty("mediaItemId").GetInt64();

        using var series = JsonDocument.Parse(await fixture.Client.GetStringAsync($"/api/tanks/{tank}/time-lapse"));
        var ids = series.RootElement.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("mediaItemId").GetInt64()).ToList();
        Assert.Equal([earlierId, laterId], ids);
    }

    [Fact]
    public async Task GivenOwnTank_WhenAddingAPhoto_ThenATankPhotoAddedEventIsRecordedInTheSameTransaction()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await AddAsync(fixture.Client, token, tank, Jpeg(600, 400));
        var photo = await BodyAsync(response);

        await using var db = fixture.CreateDbContext();
        var events = (await EventsAsync(db, "tank", tank)).Where(e => e.Type == "tank.photo_added").ToList();
        var added = Assert.Single(events);
        Assert.Equal(tank, added.Payload.GetProperty("tankId").GetInt64());
        Assert.Equal(photo.GetProperty("mediaItemId").GetInt64(), added.Payload.GetProperty("mediaItemId").GetInt64());
        var ownerId = await db.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync();
        Assert.Equal(ownerId, added.Payload.GetProperty("profileId").GetInt64());
    }

    [Fact]
    public async Task GivenOtherMemberUnknownTankOrAnonymous_WhenAdding_ThenCodedRefusalsAndNothingIsStored()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        var other = NewMemberToken();
        using var foreign = await AddAsync(fixture.Client, other, tank, Jpeg(400, 300));
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "tank_not_found");
        using var unknown = await AddAsync(fixture.Client, owner, long.MaxValue, Jpeg(400, 300));
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymous = await AddAsync(fixture.Client, null, tank, Jpeg(400, 300));
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "tank_photo_auth_required");

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.TankId == tank));
        Assert.DoesNotContain(await EventsAsync(db, "tank", tank), e => e.Type == "tank.photo_added");
    }

    [Fact]
    public async Task GivenOwnTank_WhenTheFileIsMissingNotAnImageOrUndecodable_ThenCodedBadRequest()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var empty = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos", token);
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest, "tank_photo_invalid");
        using var text = await AddAsync(fixture.Client, token, tank, "hello"u8.ToArray(), "text/plain", "a.txt");
        await AssertProblemAsync(text, HttpStatusCode.BadRequest, "tank_photo_invalid");
        using var broken = await AddAsync(fixture.Client, token, tank, UndecodableJpeg());
        await AssertProblemAsync(broken, HttpStatusCode.BadRequest, "tank_photo_invalid");
        using var huge = await AddAsync(fixture.Client, token, tank, FlatJpeg(20000, 20000));
        await AssertProblemAsync(huge, HttpStatusCode.BadRequest, "tank_photo_invalid");

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaLinks.AnyAsync(l => l.TankId == tank));
    }

    [Fact]
    public async Task GivenTheHourlyLimit_WhenAddingAnotherPhoto_ThenCodedRateLimitAndNothingIsStored()
    {
        var (token, tank) = await OwnTankAsync(fixture.ClientWithTwoUploadsPerHour);
        for (var i = 0; i < 2; i++)
        {
            using var ok = await AddAsync(fixture.ClientWithTwoUploadsPerHour, token, tank, Jpeg(300 + i, 200));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        using var third = await AddAsync(fixture.ClientWithTwoUploadsPerHour, token, tank, Jpeg(500, 300));
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests, "upload_rate_limited");

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.MediaLinks.CountAsync(l => l.TankId == tank));
        Assert.Equal(2, (await EventsAsync(db, "tank", tank)).Count(e => e.Type == "tank.photo_added"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public async Task GivenTheOriginOfTheShot_WhenAddingAPhoto_ThenOnlyAnAppShotIsMarkedAsCapturedInTheApp(string? field, bool expected)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var response = await AddAsync(fixture.Client, token, tank, PhotoFixtures.PhoneJpeg(600, 400), capturedInApp: field);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(expected, (await db.MediaLinks.Include(l => l.MediaItem).SingleAsync(l => l.TankId == tank)).MediaItem.CapturedInApp);
    }

    private static async Task<HttpResponseMessage> AddAsync(
        HttpClient client, string? token, long tank, byte[] bytes, string contentType = "image/jpeg", string fileName = "photo.jpg",
        string? capturedInApp = null)
    {
        using var content = new MultipartFormDataContent();
        if (capturedInApp is not null) content.Add(new StringContent(capturedInApp), "capturedInApp");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos") { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
