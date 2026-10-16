// Story: TASK-5.20.4
using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Media;

/// <summary>
/// Removing a link keeps the file: every path that takes a photo or video out of a place leaves the media
/// item, its renditions and its stored objects in the library of the owner, and the proof checksums keep
/// counting as used.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class MediaLinkRemovalTests(UploadsFixture fixture)
{
    private async Task<(long ItemId, List<string> Keys)> ItemOfAsync(long profileId)
    {
        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.OwnerProfileId == profileId);
        return (item.Id, [.. item.Variants.Select(v => v.StorageKey).Append(item.StorageKey)]);
    }

    private async Task AssertKeptAsync(long itemId, List<string> keys)
    {
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.MediaItems.AnyAsync(m => m.Id == itemId));
        Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == itemId));
        Assert.Equal(keys.Count - 1, await db.MediaVariants.CountAsync(v => v.MediaItemId == itemId));
        foreach (var key in keys) Assert.True(await fixture.ObjectStore.ExistsAsync(key), key);
    }

    private async Task<HttpResponseMessage> AddCarePhotoAsync(string token, long tank, long entry, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task GivenACarePhoto_WhenItIsRemovedFromTheEntry_ThenTheFileStaysInTheLibraryAndTheOldLinkStopsWorking()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        using var added = await AddCarePhotoAsync(token, tank, entry, Jpeg(640, 480));
        var body = await BodyAsync(added);
        var profileId = body.GetProperty("authorProfileId").GetInt64();
        var photo = body.GetProperty("photos")[0];
        var thumb = photo.GetProperty("urls").GetProperty("thumb").GetString()!;
        using (var served = await fixture.Client.GetAsync(thumb)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        var (itemId, keys) = await ItemOfAsync(profileId);
        long before;
        await using (var db = fixture.CreateDbContext()) before = await db.MediaItems.Where(m => m.Id == itemId).Select(m => m.VisibilityGeneration).SingleAsync();

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}/photos/{photo.GetProperty("id").GetInt64()}", token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        await AssertKeptAsync(itemId, keys);
        await using var after = fixture.CreateDbContext();
        Assert.True(await after.MediaItems.Where(m => m.Id == itemId).Select(m => m.VisibilityGeneration).SingleAsync() > before);
        using var gone = await fixture.Client.GetAsync(thumb);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "media_not_found");
    }

    [Fact]
    public async Task GivenACareEntryWithAPhoto_WhenTheEntryIsDeleted_ThenThePhotoStaysInTheLibrary()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        using var added = await AddCarePhotoAsync(token, tank, entry, Jpeg(641, 480));
        var profileId = (await BodyAsync(added)).GetProperty("authorProfileId").GetInt64();
        var (itemId, keys) = await ItemOfAsync(profileId);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        await AssertKeptAsync(itemId, keys);
    }

    [Fact]
    public async Task GivenADraftWithAPhoto_WhenTheDraftIsDiscarded_ThenThePhotoStaysInTheLibrary()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(642, 480));
        await using var db = fixture.CreateDbContext();
        var itemId = await db.MediaLinks.Where(l => l.StoryId == draft.Id).Select(l => l.MediaItemId).SingleAsync();
        var keys = await db.MediaVariants.Where(v => v.MediaItemId == itemId).Select(v => v.StorageKey)
            .Concat(db.MediaItems.Where(m => m.Id == itemId).Select(m => m.StorageKey)).ToListAsync();

        using var discarded = await DiscardAsync(fixture.Client, token, draft.Id);
        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);

        await AssertKeptAsync(itemId, keys);
    }

    [Fact]
    public async Task GivenAnExpiredDraft_WhenTheCleanupRuns_ThenThePhotoStaysInTheLibrary()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(643, 480));
        long itemId;
        List<string> keys;
        await using (var db = fixture.CreateDbContext())
        {
            itemId = await db.MediaLinks.Where(l => l.StoryId == draft.Id).Select(l => l.MediaItemId).SingleAsync();
            keys = await db.MediaVariants.Where(v => v.MediaItemId == itemId).Select(v => v.StorageKey)
                .Concat(db.MediaItems.Where(m => m.Id == itemId).Select(m => m.StorageKey)).ToListAsync();
            await db.Stories.Where(s => s.Id == draft.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-400)));
        }

        using var scope = fixture.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None) >= 1);

        await AssertKeptAsync(itemId, keys);
    }

    [Fact]
    public async Task GivenAnOpenWaterChangeWithAPhoto_WhenItIsCancelled_ThenThePhotoStaysInTheLibrary()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var start = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/water-changes", token);
        var id = (await BodyAsync(start)).GetProperty("id").GetInt64();
        using (var content = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(Jpeg(644, 480));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(file, "file", "photo.jpg");
            content.Add(new StringContent("true"), "capturedInApp");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/water-changes/{id}/photos") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var photo = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        }

        long profileId;
        await using (var db = fixture.CreateDbContext()) profileId = await db.WaterChangeSessions.Where(s => s.Id == id).Select(s => s.ProfileId).SingleAsync();
        var (itemId, keys) = await ItemOfAsync(profileId);

        using var cancel = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/water-changes/{id}", token);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        await AssertKeptAsync(itemId, keys);
    }

    [Fact]
    public async Task GivenACareVideoAndATankVideo_WhenTheirLinksAreRemoved_ThenTheVideosStayInTheLibrary()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        var careVideo = (await UploadedAsync(fixture.Client, token, await SmallVideo, tank, entry)).GetProperty("id").GetInt64();
        var tankVideo = (await UploadedAsync(fixture.Client, token, await SmallVideo, tank)).GetProperty("id").GetInt64();

        using var careRemoved = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}/videos/{careVideo}", token);
        Assert.Equal(HttpStatusCode.OK, careRemoved.StatusCode);
        using var tankRemoved = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/videos/{tankVideo}", token);
        Assert.Equal(HttpStatusCode.NoContent, tankRemoved.StatusCode);

        await using var db = fixture.CreateDbContext();
        foreach (var id in new[] { careVideo, tankVideo })
        {
            var item = await db.MediaItems.SingleAsync(m => m.Id == id);
            Assert.Equal(MediaKind.Video, item.Kind);
            Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == id));
            Assert.True(await fixture.ObjectStore.ExistsAsync(item.StorageKey));
        }
    }

    [Fact]
    public async Task GivenAPhotoOfAFinishedWaterChange_WhenTheEntryIsDeleted_ThenThePictureStaysUsedBefore()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var first = await FinishWithPhotoAsync(token, tank, Jpeg(645, 480));
        Assert.Equal("verified", first);
        using var entries = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var entry = (await BodyAsync(entries)).GetProperty("items")[0].GetProperty("id").GetInt64();
        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Equal("partial", await FinishWithPhotoAsync(token, tank, Jpeg(645, 480)));
    }

    [Fact]
    public async Task GivenACarePhotoThatWasRemoved_WhenTheSamePictureProvesADecommissioning_ThenItCountsAsUsedBefore()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        var bytes = Jpeg(646, 480);
        using var added = await AddCarePhotoAsync(token, tank, entry, bytes);
        var photoId = (await BodyAsync(added)).GetProperty("photos")[0].GetProperty("id").GetInt64();
        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}/photos/{photoId}", token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        content.Add(new StringContent("true"), "capturedInApp");
        content.Add(new StringContent("all_care"), "scope");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/tanks/{tank}/decommissioning?date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&utcOffsetMinutes=0") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "decommission_photo_reused");
    }

    private async Task<string> FinishWithPhotoAsync(string token, long tank, byte[] bytes)
    {
        using var start = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/water-changes", token);
        var id = (await BodyAsync(start)).GetProperty("id").GetInt64();
        using (var drain = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/water-changes/{id}/steps/draining_started", token, new { state = "done" }))
            Assert.Equal(HttpStatusCode.OK, drain.StatusCode);
        using (var content = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(file, "file", "photo.jpg");
            content.Add(new StringContent("true"), "capturedInApp");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/water-changes/{id}/photos") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var photo = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        }

        using (var refill = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/water-changes/{id}/steps/refill_started", token, new { state = "done" }))
            Assert.Equal(HttpStatusCode.OK, refill.StatusCode);
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step = 'refill_started'");

        using var finished = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/water-changes/{id}/finish", token, new { });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        return (await BodyAsync(finished)).GetProperty("entry").GetProperty("evidence").GetProperty("state").GetString()!;
    }
}
