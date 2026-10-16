// Story: TASK-5.10
using System.Net;
using Cichlids.Api.Features.Videos;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Videos;

/// <summary>
/// The resumable upload of a video: sessions, chunks, resuming, the size and offset guards, the
/// probe and the length policy at completion, the upload quota and the cleanup of abandoned sessions.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class VideoUploadTests(UploadsFixture fixture)
{
    private static readonly Task<byte[]> LargeVideo = TestVideos.CreateAsync(seconds: 2, padToBytes: 9L * 1024 * 1024);

    [Fact]
    public async Task GivenALargeVideo_WhenTheConnectionBreaksBetweenChunks_ThenTheUploadResumesAtTheStoredOffset()
    {
        var video = await LargeVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);

        using var first = await PatchAsync(fixture.Client, token, id, 0, video[..Chunk]);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(Chunk.ToString(), first.Headers.GetValues("Upload-Offset").Single());

        using var status = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/video-uploads/{id}", token);
        var session = await BodyAsync(status);
        Assert.Equal(Chunk, session.GetProperty("offset").GetInt64());
        Assert.Equal(video.Length, session.GetProperty("size").GetInt64());
        Assert.Equal(Chunk, session.GetProperty("chunkSize").GetInt32());

        using var replay = await PatchAsync(fixture.Client, token, id, 0, video[..Chunk]);
        var mismatch = await ProblemAsync(replay, HttpStatusCode.Conflict, "video_upload_offset_mismatch");
        Assert.Equal(Chunk, mismatch.GetProperty("offset").GetInt64());

        using var last = await PatchAsync(fixture.Client, token, id, Chunk, video[Chunk..]);
        Assert.Equal(HttpStatusCode.NoContent, last.StatusCode);
        using var completed = await CompleteAsync(fixture.Client, token, id);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        var created = await BodyAsync(completed);
        Assert.Equal("processing", created.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GivenAShortVideoToATank_WhenProcessed_ThenItIsReadyAsPublicHlsWithAPosterAndThePrivateOriginal()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var created = await UploadedAsync(fixture.Client, token, await SmallVideo, tank);
        var mediaItemId = created.GetProperty("id").GetInt64();

        using var before = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        Assert.Equal("processing", (await BodyAsync(before))[0].GetProperty("state").GetString());
        using var anonymousBefore = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", null);
        Assert.Equal(0, (await BodyAsync(anonymousBefore)).GetArrayLength());

        await ProcessQueueAsync(fixture.Services);

        using var after = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", null);
        var video = Assert.Single((await BodyAsync(after)).EnumerateArray());
        Assert.Equal(("ready", mediaItemId), (video.GetProperty("state").GetString(), video.GetProperty("id").GetInt64()));
        Assert.InRange(video.GetProperty("durationSeconds").GetDouble(), 1.5, 2.5);
        Assert.Equal(320, video.GetProperty("width").GetInt32());
        using var anonymous = new HttpClient();
        var playlist = await anonymous.GetStringAsync(video.GetProperty("playlistUrl").GetString());
        Assert.StartsWith("#EXTM3U", playlist);
        Assert.Contains("180p/index.m3u8", playlist);
        using var poster = await anonymous.GetAsync(video.GetProperty("posterUrl").GetString());
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);
        Assert.True((await poster.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]));

        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == mediaItemId);
        Assert.Equal(MediaKind.Video, item.Kind);
        Assert.StartsWith("private/video/", item.StorageKey);
        Assert.InRange(item.DurationMs!.Value, 1500, 2500);
        Assert.All(item.Variants, v => Assert.StartsWith("variants/video/", v.StorageKey));
        Assert.Equal(["hls", "poster"], item.Variants.Select(v => v.Label).Order());
        using var original = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{item.StorageKey}");
        Assert.Equal(HttpStatusCode.Forbidden, original.StatusCode);
        Assert.Equal(VideoJobStatus.Done, (await db.VideoJobs.SingleAsync(j => j.MediaItemId == mediaItemId)).Status);
        Assert.Equal(MediaLinkRole.Video, (await db.MediaLinks.SingleAsync(l => l.MediaItemId == mediaItemId && l.TankId != null)).Role);
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "video" && e.AggregateId == mediaItemId.ToString())
            .Select(e => e.EventType).ToListAsync();
        Assert.Equal([VideoEvents.Processed, VideoEvents.UploadCompleted], events.Order());
    }

    [Fact]
    public async Task GivenAVideoOverTheMemberLimit_WhenCompleting_ThenItIsRefusedAndNothingIsKept()
    {
        var (token, tank) = await OwnTankAsync(fixture.ClientWithOneSecondVideoLimit);
        var video = await SmallVideo;
        var id = await OpenSessionAsync(fixture.ClientWithOneSecondVideoLimit, token, video.Length, tank);
        using var patch = await PatchAsync(fixture.ClientWithOneSecondVideoLimit, token, id, 0, video);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        using var completed = await CompleteAsync(fixture.ClientWithOneSecondVideoLimit, token, id);

        await ProblemAsync(completed, HttpStatusCode.UnprocessableEntity, "video_too_long");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.VideoUploads.AnyAsync(u => u.Id == id));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.TankId == tank));
        using var gone = await SendAsync(fixture.ClientWithOneSecondVideoLimit, HttpMethod.Get, $"/api/video-uploads/{id}", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task GivenAFileThatIsNoVideo_WhenSendingTheFirstChunk_ThenItIsRefusedAndTheSessionEnds()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var bytes = "GIF89a this is not a video, whatever the content type says"u8.ToArray();
        var id = await OpenSessionAsync(fixture.Client, token, bytes.Length, tank);

        using var patch = await PatchAsync(fixture.Client, token, id, 0, bytes);

        await ProblemAsync(patch, HttpStatusCode.BadRequest, "video_format_unsupported");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.VideoUploads.AnyAsync(u => u.Id == id));
    }

    [Theory]
    [InlineData("ftyp-garbage")]
    [InlineData("codec")]
    public async Task GivenACorruptOrUnsupportedVideo_WhenCompleting_ThenCodedRefusal(string kind)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var bytes = kind == "codec"
            ? await TestVideos.CreateAsync(seconds: 1, audio: false, codec: "mpeg4")
            : [.. new byte[] { 0, 0, 0, 24 }, .. "ftypisom"u8.ToArray(), .. new byte[] { 0, 0, 2, 0 }, .. "isomiso2"u8.ToArray(), .. new byte[100]];
        var id = await OpenSessionAsync(fixture.Client, token, bytes.Length, tank);
        using var patch = await PatchAsync(fixture.Client, token, id, 0, bytes);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        using var completed = await CompleteAsync(fixture.Client, token, id);

        await ProblemAsync(completed, HttpStatusCode.UnprocessableEntity, kind == "codec" ? "video_codec_unsupported" : "video_unreadable");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaItems.AnyAsync(m => m.Kind == MediaKind.Video && m.OwnerProfileId == null));
        Assert.False(await db.VideoUploads.AnyAsync(u => u.Id == id));
    }

    [Fact]
    public async Task GivenAChunkLargerThanTheChunkSize_ThenItIsRefusedWithoutChangingTheOffset()
    {
        var video = await LargeVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);

        using var tooLarge = await PatchAsync(fixture.Client, token, id, 0, video[..(Chunk + 1)]);

        await ProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "video_chunk_too_large");
        await AssertOffsetAsync(token, id, 0);
    }

    [Fact]
    public async Task GivenMoreBytesThanTheAnnouncedSize_ThenTheChunkIsRefusedAndNothingIsStored()
    {
        var video = await SmallVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);

        using var tooMany = await PatchAsync(fixture.Client, token, id, 0, [.. video, 0]);

        await ProblemAsync(tooMany, HttpStatusCode.RequestEntityTooLarge, "video_chunk_too_large");
        await AssertOffsetAsync(token, id, 0);
    }

    [Fact]
    public async Task GivenAChunkedBodyWithoutALength_WhenItExceedsTheAnnouncedSize_ThenItIsRefusedWithoutStoringAnything()
    {
        var video = await SmallVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/video-uploads/{id}")
        {
            Content = new StreamContent(new ForwardOnlyStream([.. video, .. new byte[50_000]])),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Upload-Offset", "0");
        Assert.Null(request.Content.Headers.ContentLength);

        using var response = await fixture.Client.SendAsync(request);

        await ProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "video_chunk_too_large");
        await AssertOffsetAsync(token, id, 0);
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task GivenAShortChunk_ThenItIsRefusedBecauseOnlyTheLastChunkMayBeShort()
    {
        var video = await LargeVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);

        using var shortChunk = await PatchAsync(fixture.Client, token, id, 0, video[..(Chunk - 1)]);

        await ProblemAsync(shortChunk, HttpStatusCode.BadRequest, "video_chunk_incomplete");
        await AssertOffsetAsync(token, id, 0);
    }

    [Fact]
    public async Task GivenAFullyReceivedUpload_ThenNoFurtherChunkIsAcceptedBeforeOrAfterCompletion()
    {
        var video = await SmallVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);
        using var patch = await PatchAsync(fixture.Client, token, id, 0, video);
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        using var before = await PatchAsync(fixture.Client, token, id, video.Length, [1, 2, 3]);
        await ProblemAsync(before, HttpStatusCode.Conflict, "video_upload_complete");
        using var completed = await CompleteAsync(fixture.Client, token, id);
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);

        using var after = await PatchAsync(fixture.Client, token, id, video.Length, [1, 2, 3]);
        await ProblemAsync(after, HttpStatusCode.NotFound, "video_upload_not_found");
        using var again = await CompleteAsync(fixture.Client, token, id);
        await ProblemAsync(again, HttpStatusCode.NotFound, "video_upload_not_found");
    }

    [Fact]
    public async Task GivenAnUnfinishedUpload_WhenCompleting_ThenItIsRefused()
    {
        var video = await LargeVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);
        using var patch = await PatchAsync(fixture.Client, token, id, 0, video[..Chunk]);

        using var completed = await CompleteAsync(fixture.Client, token, id);

        await ProblemAsync(completed, HttpStatusCode.Conflict, "video_upload_incomplete");
    }

    [Fact]
    public async Task GivenASizeAboveTheMemberLimit_WhenOpeningASession_ThenItIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        const long over = (60L * 25_000_000 / 8) + 1;

        using var response = await CreateSessionAsync(fixture.Client, token, over, tank);
        await ProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "video_too_large");
        using var empty = await CreateSessionAsync(fixture.Client, token, 0, tank);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task GivenOtherMembersAndAnonymousCallers_ThenSessionsAndTargetsAreOutOfReach()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, owner, 1000, tank);
        var other = NewMemberToken();

        using var patch = await PatchAsync(fixture.Client, other, id, 0, new byte[1000]);
        await ProblemAsync(patch, HttpStatusCode.NotFound, "video_upload_not_found");
        using var status = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/video-uploads/{id}", other);
        await ProblemAsync(status, HttpStatusCode.NotFound, "video_upload_not_found");
        using var foreignTank = await CreateSessionAsync(fixture.Client, other, 1000, tank);
        await ProblemAsync(foreignTank, HttpStatusCode.NotFound, "video_target_not_found");
        using var anonymous = await CreateSessionAsync(fixture.Client, null, 1000, tank);
        await ProblemAsync(anonymous, HttpStatusCode.Unauthorized, "video_auth_required");
        using var anonymousPatch = await PatchAsync(fixture.Client, null, id, 0, new byte[10]);
        await ProblemAsync(anonymousPatch, HttpStatusCode.Unauthorized, "video_auth_required");
    }

    [Fact]
    public async Task GivenTheHourlyUploadLimit_ThenCompletedVideosCountAndTheNextSessionIsRefused()
    {
        var client = fixture.ClientWithTwoUploadsPerHour;
        var (token, tank) = await OwnTankAsync(client);
        for (var i = 0; i < 2; i++)
        {
            await UploadedAsync(client, token, await SmallVideo, tank);
        }

        using var refused = await CreateSessionAsync(client, token, 1000, tank);

        await ProblemAsync(refused, HttpStatusCode.TooManyRequests, "upload_rate_limited");
    }

    [Fact]
    public async Task GivenTooManyOpenSessions_ThenAnotherOneIsRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        for (var i = 0; i < 3; i++)
        {
            await OpenSessionAsync(fixture.Client, token, 1000, tank);
        }

        using var refused = await CreateSessionAsync(fixture.Client, token, 1000, tank);

        await ProblemAsync(refused, HttpStatusCode.Conflict, "video_upload_limit_reached");
    }

    [Fact]
    public async Task GivenAnAbandonedSession_WhenItExpires_ThenCleanupRemovesItAndItsParts()
    {
        var video = await LargeVideo;
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, video.Length, tank);
        using var patch = await PatchAsync(fixture.Client, token, id, 0, video[..Chunk]);
        await using (var db = fixture.CreateDbContext())
        {
            await db.VideoUploads.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        using (var late = await PatchAsync(fixture.Client, token, id, Chunk, video[Chunk..]))
        {
            await ProblemAsync(late, HttpStatusCode.Gone, "video_upload_expired");
        }

        int removed;
        using (var scope = fixture.Services.CreateScope())
        {
            removed = await scope.ServiceProvider.GetRequiredService<VideoUploadCleanupService>().RunOnceAsync(CancellationToken.None);
        }

        Assert.True(removed >= 1);
        await using var check = fixture.CreateDbContext();
        Assert.False(await check.VideoUploads.AnyAsync(u => u.Id == id));
    }

    [Fact]
    public async Task GivenAnOpenSession_WhenTheMemberDiscardsIt_ThenItIsGone()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await OpenSessionAsync(fixture.Client, token, 1000, tank);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/video-uploads/{id}", token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var status = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/video-uploads/{id}", token);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }

    private async Task AssertOffsetAsync(string token, long id, long expected)
    {
        using var status = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/video-uploads/{id}", token);
        Assert.Equal(expected, (await BodyAsync(status)).GetProperty("offset").GetInt64());
    }
}
