// Story: TASK-5.10
using System.Net;
using Cichlids.Api.Features.Videos;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Videos;

/// <summary>
/// Videos attached to tanks and care entries: public HLS for tank videos, private segments behind
/// a signed path for care videos, the failed state, removal and the exclusion from photo series.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class VideoAttachmentTests(UploadsFixture fixture)
{
    [Fact]
    public async Task GivenACareVideo_ThenOnlyTheSignedPathServesItsFilesAndTheStoreRefusesEverythingDirect()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client, isPublic: false);
        var entry = await LogAsync(fixture.Client, token, tank);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank, entry);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await ProcessQueueAsync(fixture.Services);

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var video = Assert.Single((await BodyAsync(list)).GetProperty("items")[0].GetProperty("videos").EnumerateArray());
        Assert.Equal("ready", video.GetProperty("state").GetString());
        var playlistUrl = video.GetProperty("playlistUrl").GetString()!;
        Assert.StartsWith("/api/media/videos/", playlistUrl);
        Assert.EndsWith("/master.m3u8", playlistUrl);
        var baseUrl = playlistUrl[..^"master.m3u8".Length];

        using var master = await fixture.Client.GetAsync(playlistUrl);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", master.Headers.CacheControl?.ToString());
        var masterText = await master.Content.ReadAsStringAsync();
        Assert.DoesNotContain("http", masterText);
        var rendition = masterText.Split('\n').Single(l => l.EndsWith("index.m3u8", StringComparison.Ordinal));
        var renditionText = await fixture.Client.GetStringAsync(baseUrl + rendition);
        var segment = renditionText.Split('\n').First(l => l.EndsWith(".m4s", StringComparison.Ordinal));
        var init = renditionText.Split('\n').First(l => l.StartsWith("#EXT-X-MAP", StringComparison.Ordinal)).Split('"')[1];
        var directory = rendition[..(rendition.LastIndexOf('/') + 1)];
        using var segmentResponse = await fixture.Client.GetAsync(baseUrl + directory + segment);
        Assert.Equal(HttpStatusCode.OK, segmentResponse.StatusCode);
        Assert.NotEmpty(await segmentResponse.Content.ReadAsByteArrayAsync());
        using var initResponse = await fixture.Client.GetAsync(baseUrl + directory + init);
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);
        using var poster = await fixture.Client.GetAsync(video.GetProperty("posterUrl").GetString());
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);

        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == mediaItemId);
        Assert.StartsWith("private/video/", item.StorageKey);
        Assert.All(item.Variants, v => Assert.StartsWith("private/variants/video/", v.StorageKey));
        var hls = item.Variants.Single(v => v.Label == "hls").StorageKey;
        var keys = new[] { item.StorageKey, hls, hls.Replace("master.m3u8", directory + segment, StringComparison.Ordinal) };
        using var anonymous = new HttpClient();
        foreach (var key in keys)
        {
            using var direct = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
            Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        }

        var tokenPart = baseUrl["/api/media/videos/".Length..].TrimEnd('/');
        foreach (var path in new[]
        {
            $"{tokenPart[..^3]}AAA/master.m3u8", "not-a-token/master.m3u8", $"{tokenPart}/../master.m3u8", $"{tokenPart}/%2e%2e/x",
            $"{tokenPart}/480p/../../x", $"{tokenPart}/unknown.txt", $"{tokenPart}/{directory}evil.m4s", $"{tokenPart}/{directory}seg_1.m4s",
        })
        {
            using var refused = await fixture.Client.GetAsync($"/api/media/videos/{path}");
            Assert.True(refused.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"{path} answered {refused.StatusCode}");
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}/videos/{mediaItemId}", token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var afterRemoval = await fixture.Client.GetAsync(playlistUrl);
        Assert.Equal(HttpStatusCode.NotFound, afterRemoval.StatusCode);
        Assert.True(await fixture.ObjectStore.ExistsAsync(item.StorageKey));
        Assert.True(await fixture.ObjectStore.ExistsAsync(hls));
    }

    [Fact]
    public async Task GivenACareEntryOfAnotherMember_ThenAVideoCannotBeAttachedToIt()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, owner, tank);
        var other = NewMemberToken();

        using var response = await CreateSessionAsync(fixture.Client, other, 1000, tank, entry);

        await ProblemAsync(response, HttpStatusCode.NotFound, "video_target_not_found");
    }

    [Fact]
    public async Task GivenMoreCareVideosThanAnEntryHolds_ThenTheNextOneIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        for (var i = 0; i < 2; i++)
        {
            await UploadedAsync(fixture.Client, token, await SmallVideo, tank, entry);
        }

        using var third = await CreateSessionAsync(fixture.Client, token, 1000, tank, entry);

        await ProblemAsync(third, HttpStatusCode.Conflict, "care_video_limit_reached");
    }

    [Fact]
    public async Task GivenAFailedJob_ThenTheOwnerSeesAFailedVideoAndOthersSeeNothing()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await using (var db = fixture.CreateDbContext())
        {
            await db.VideoJobs.Where(j => j.MediaItemId == mediaItemId).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, VideoJobStatus.Failed).SetProperty(j => j.ErrorCode, VideoProcessingErrors.ProcessingFailed));
        }

        using var own = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        var video = Assert.Single((await BodyAsync(own)).EnumerateArray());
        Assert.Equal(("failed", VideoProcessingErrors.ProcessingFailed), (video.GetProperty("state").GetString(), video.GetProperty("errorCode").GetString()));
        Assert.Equal(JsonValueKindNull, video.GetProperty("playlistUrl").ValueKind.ToString());
        using var anonymous = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", null);
        Assert.Equal(0, (await BodyAsync(anonymous)).GetArrayLength());
        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/videos/{mediaItemId}", token);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var after = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        Assert.Equal(0, (await BodyAsync(after)).GetArrayLength());
    }

    private const string JsonValueKindNull = "Null";

    [Fact]
    public async Task GivenATankWithAVideo_ThenThePhotoSeriesStaysPhotosOnlyAndTheTimelineListsTheReadyVideo()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        await ProcessQueueAsync(fixture.Services);

        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        var body = await BodyAsync(series);

        Assert.Equal(0, body.GetProperty("photos").GetArrayLength());
        var video = Assert.Single(body.GetProperty("videos").EnumerateArray());
        Assert.StartsWith("http", video.GetProperty("playlistUrl").GetString());
        Assert.StartsWith("http", video.GetProperty("posterUrl").GetString());
    }

    [Fact]
    public async Task GivenAnotherMembersTank_ThenAVideoCannotBeRemovedOrListedAsFailed()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/videos/{mediaItemId}", NewMemberToken());
        await ProblemAsync(removed, HttpStatusCode.NotFound, "video_not_found");
        using var others = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", NewMemberToken());
        Assert.Equal(0, (await BodyAsync(others)).GetArrayLength());
    }

    [Fact]
    public async Task GivenAProcessingFailure_ThenTheJobIsRetriedAndFinallyMarkedFailed()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();
        await using (var db = fixture.CreateDbContext())
        {
            var item = await db.MediaItems.SingleAsync(m => m.Id == mediaItemId);
            await fixture.ObjectStore.DeleteAsync(item.StorageKey);
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using (var db = fixture.CreateDbContext())
            {
                await db.VideoJobs.Where(j => j.MediaItemId == mediaItemId).ExecuteUpdateAsync(s => s.SetProperty(j => j.RunAfter, DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            await ProcessQueueAsync(fixture.Services);
        }

        await using var check = fixture.CreateDbContext();
        var job = await check.VideoJobs.SingleAsync(j => j.MediaItemId == mediaItemId);
        Assert.Equal((VideoJobStatus.Failed, 3), (job.Status, job.Attempts));
        using var own = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        Assert.Equal("failed", (await BodyAsync(own))[0].GetProperty("state").GetString());
    }
}
