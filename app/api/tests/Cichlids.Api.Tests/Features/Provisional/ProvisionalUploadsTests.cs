// Story: TASK-7.2
using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Videos;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// Photos, videos and the media library of a provisional caller: the files exist and stay private,
/// and the upload limits of provisional profiles are lower than those of members.
/// </summary>
[Collection(UploadsCollection.Name)]
[Trait("Story", "TASK-7.2")]
public sealed class ProvisionalUploadsTests(UploadsFixture fixture)
{
    private static async Task<HttpResponseMessage> UploadToLibraryAsync(HttpClient client, string token, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "library.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/media/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenCreatingATankAndLoggingAWaterChangeWithAPhoto_ThenBothExistAndArePrivate()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        var entry = await LogAsync(fixture.Client, caller.Token, tank, new { kind = "water_change" });

        using var added = await SendPhotoAsync(caller.Token, $"/api/tanks/{tank}/care/{entry}/photos", Jpeg(1200, 800));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var photo = Assert.Single((await BodyAsync(added)).GetProperty("photos").EnumerateArray());
        var thumb = photo.GetProperty("urls").GetProperty("thumb").GetString()!;
        Assert.StartsWith("/api/media/photos/", thumb);
        using var served = await fixture.Client.GetAsync(thumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("private", served.Headers.CacheControl?.ToString());

        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(TankVisibility.Private, stored.ProfileVisibility);
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.OwnerProfileId == caller.ProfileId);
        Assert.StartsWith("private/", item.StorageKey);
        Assert.All(item.Variants, v => Assert.StartsWith("private/", v.StorageKey));

        using var member = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", NewMemberToken());
        using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", null);
        Assert.Equal(HttpStatusCode.NotFound, member.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenAddingATimeLapsePhoto_ThenItJoinsTheSeriesOfTheOwnerOnly()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);

        using var added = await SendPhotoAsync(caller.Token, $"/api/tanks/{tank}/time-lapse/photos", PhotoFixtures.PhoneJpeg(1200, 800));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.StartsWith("/api/media/photos/", (await BodyAsync(added)).GetProperty("image").GetProperty("thumb").GetString());
        using var own = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", caller.Token);
        Assert.Single((await BodyAsync(own)).GetProperty("photos").EnumerateArray());
        using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenUsingTheMediaLibrary_ThenItUploadsListsAndDeletesItsFiles()
    {
        using var caller = await NewAsync(fixture.Client);

        using var uploaded = await UploadToLibraryAsync(fixture.Client, caller.Token, Jpeg(900, 600));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await BodyAsync(uploaded)).GetProperty("id").GetInt64();
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", caller.Token);
        Assert.Equal(id, Assert.Single((await BodyAsync(list)).GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());
        using var memberList = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media", NewMemberToken());
        Assert.Empty((await BodyAsync(memberList)).GetProperty("items").EnumerateArray());

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaItems.AnyAsync(m => m.Id == id));
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenUploadingAPhotoForAStory_ThenTheApiAnswersRegistrationRequiredAndStoresNothing()
    {
        using var caller = await NewAsync(fixture.Client);

        using var response = await UploadAsync(fixture.Client, caller.Token, Jpeg(300, 200), "image/jpeg");

        await AssertRegistrationRequiredAsync(response);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaItems.AnyAsync(m => m.OwnerProfileId == caller.ProfileId));
        Assert.False(await db.Stories.AnyAsync(s => s.AuthorProfileId == caller.ProfileId));
    }

    [Fact]
    public async Task GivenALowerHourlyLimitForProvisionalProfiles_WhenAProvisionalCallerUploadsOnePhotoTooMany_ThenItIsRateLimitedAndAMemberIsNot()
    {
        await using var host = fixture.CreateHost(new Dictionary<string, string?> { ["UploadLimits:ProvisionalMaxUploadsPerHour"] = "2" }, null);
        using var client = host.CreateClient();
        using var caller = await NewAsync(client);
        var member = NewMemberToken();

        for (var i = 0; i < 2; i++)
        {
            using var accepted = await UploadToLibraryAsync(client, caller.Token, Jpeg(300 + i, 200));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var limited = await UploadToLibraryAsync(client, caller.Token, Jpeg(310, 200));
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "upload_rate_limited");
        for (var i = 0; i < 3; i++)
        {
            using var accepted = await UploadToLibraryAsync(client, member, Jpeg(320 + i, 200));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task GivenTheDefaultLimits_WhenAProvisionalCallerOpensVideoUploads_ThenItHoldsOneOpenUploadAndAMemberThree()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        var (memberToken, memberTank) = await OwnTankAsync(fixture.Client);

        using var first = await CreateSessionAsync(fixture.Client, caller.Token, 1_000_000, tank);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await CreateSessionAsync(fixture.Client, caller.Token, 1_000_000, tank);
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "video_upload_limit_reached");

        for (var i = 0; i < 3; i++)
        {
            using var accepted = await CreateSessionAsync(fixture.Client, memberToken, 1_000_000, memberTank);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task GivenTheDefaultLimits_WhenAProvisionalCallerAnnouncesALongVideo_ThenTheLengthLimitOfThirtySecondsApplies()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        var (memberToken, memberTank) = await OwnTankAsync(fixture.Client);
        const long aboutFortySeconds = 125_000_000;

        using var refused = await CreateSessionAsync(fixture.Client, caller.Token, aboutFortySeconds, tank);
        using var accepted = await CreateSessionAsync(fixture.Client, memberToken, aboutFortySeconds, memberTank);

        await AssertProblemAsync(refused, HttpStatusCode.RequestEntityTooLarge, "video_too_large");
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<VideoLimitPolicy>();
        Assert.Equal(TimeSpan.FromSeconds(30), (await policy.ResolveAsync(caller.ProfileId, CancellationToken.None)).MaxDuration);
    }

    [Fact]
    public async Task GivenALimitOnProvisionalVideos_WhenAProvisionalCallerHoldsThatMany_ThenAnotherUploadIsRefused()
    {
        await using var host = fixture.CreateHost(new Dictionary<string, string?> { ["VideoLimits:ProvisionalMaxVideos"] = "1" }, null);
        using var client = host.CreateClient();
        using var caller = await NewAsync(client);
        var tank = await CreateTankAsync(client, caller);
        await using (var db = fixture.CreateDbContext())
        {
            db.MediaItems.Add(new MediaItem
            {
                OwnerProfileId = caller.ProfileId, Kind = MediaKind.Video, StorageKey = $"private/test/{Guid.NewGuid():N}.mp4",
                CreatedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var response = await CreateSessionAsync(client, caller.Token, 1_000_000, tank);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "video_limit_reached");
    }

    private async Task<HttpResponseMessage> SendPhotoAsync(string token, string url, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }
}
