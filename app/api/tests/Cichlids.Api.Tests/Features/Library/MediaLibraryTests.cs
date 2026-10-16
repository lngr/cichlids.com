// Story: TASK-5.20.4
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Stories;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Library;

/// <summary>
/// The media library of a member: files without links, the list with filters, the places of a file,
/// uploads into the library and the deletion that removes a file with its renditions and all its links.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class MediaLibraryTests(UploadsFixture fixture)
{
    internal static async Task<HttpResponseMessage> UploadToLibraryAsync(HttpClient client, string? token, byte[] bytes, string contentType = "image/jpeg")
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", "library.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/media/photos") { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<long> LibraryPhotoAsync(string token, byte[] bytes)
    {
        using var response = await UploadToLibraryAsync(fixture.Client, token, bytes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> ListAsync(string token, string query = "")
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media" + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    private static List<long> IdsOf(JsonElement list) => [.. list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64())];

    private async Task<long> TankWithTitleAsync(string token, string title)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/tanks", token, new { title, isPublic = true });
        response.EnsureSuccessStatusCode();
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    private async Task<long> SeriesPhotoAsync(string token, long tank, bool capturedInApp = false, int width = 700)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(width, 500));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        if (capturedInApp) content.Add(new StringContent("true"), "capturedInApp");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("mediaItemId").GetInt64();
    }

    // Publishes a story whose first item is the given media item and adds the given others as further items.
    private async Task<long> PublishedStoryWithAsync(string token, string title, long firstItem, params long[] others)
    {
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        await using (var db = fixture.CreateDbContext())
        {
            var own = await db.MediaLinks.Where(l => l.StoryId == draft.Id).SingleAsync();
            db.MediaLinks.Remove(own);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForStory(draft.Id, firstItem, 0));
            var position = 1;
            foreach (var other in others) db.MediaLinks.Add(MediaLink.ForStory(draft.Id, other, position++));
            await db.SaveChangesAsync();
        }

        using var publish = await PublishAsync(fixture.Client, token, draft.Id, new PublishStoryRequest(title, null, "cichlids"));
        publish.EnsureSuccessStatusCode();
        return draft.Id;
    }

    [Fact]
    public async Task GivenAPhotoUploadedIntoTheLibrary_ThenItIsPrivateWithoutLinksListedForTheOwnerAndServedThroughASignedLink()
    {
        var token = NewMemberToken();
        using var response = await UploadToLibraryAsync(fixture.Client, token, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await BodyAsync(response);
        var id = item.GetProperty("id").GetInt64();
        Assert.Equal("photo", item.GetProperty("kind").GetString());
        Assert.Equal(0, item.GetProperty("placeCount").GetInt32());
        var thumb = item.GetProperty("image").GetProperty("thumb").GetString()!;
        Assert.StartsWith("/api/media/photos/", thumb);

        await using var db = fixture.CreateDbContext();
        var stored = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == id);
        Assert.False(await db.MediaLinks.AnyAsync(l => l.MediaItemId == id));
        Assert.StartsWith("private/", stored.StorageKey);
        Assert.All(stored.Variants, v => Assert.StartsWith("private/", v.StorageKey));
        using (var served = await fixture.Client.GetAsync(thumb)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        using var anonymous = new HttpClient();
        foreach (var key in stored.Variants.Select(v => v.StorageKey).Append(stored.StorageKey))
        {
            using var direct = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
            Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        }

        Assert.Contains(id, IdsOf(await ListAsync(token, "?filter=unlinked")));
        Assert.DoesNotContain(id, IdsOf(await ListAsync(token, "?filter=linked")));
    }

    [Fact]
    public async Task GivenAFileThatIsNoPhoto_WhenUploadingIntoTheLibrary_ThenCodedRefusalAndNothingIsStored()
    {
        var token = NewMemberToken();
        using var text = await UploadToLibraryAsync(fixture.Client, token, "no photo"u8.ToArray(), "text/plain");
        await AssertProblemAsync(text, HttpStatusCode.BadRequest, "library_photo_invalid");
        using var broken = await UploadToLibraryAsync(fixture.Client, token, UndecodableJpeg());
        await AssertProblemAsync(broken, HttpStatusCode.BadRequest, "library_photo_invalid");
        using var empty = await UploadToLibraryAsync(fixture.Client, token, []);
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest, "library_photo_invalid");
        Assert.Equal(0, (await ListAsync(token)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GivenTheHourlyUploadLimit_WhenUploadingIntoTheLibrary_ThenLibraryFilesCountAgainstIt()
    {
        var token = NewMemberToken();
        for (var i = 0; i < 2; i++)
        {
            using var ok = await UploadToLibraryAsync(fixture.ClientWithTwoUploadsPerHour, token, Jpeg(300 + i, 200));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var third = await UploadToLibraryAsync(fixture.ClientWithTwoUploadsPerHour, token, Jpeg(310, 200));
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests, "upload_rate_limited");
        using var draft = await UploadAsync(fixture.ClientWithTwoUploadsPerHour, token, Jpeg(320, 200), "image/jpeg");
        Assert.Equal(HttpStatusCode.TooManyRequests, draft.StatusCode);
        Assert.Equal(2, (await ListAsync(token)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GivenFilesInManyStates_WhenListingTheLibrary_ThenTheyComeNewestFirstPagedAndFiltered()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var unlinked = await LibraryPhotoAsync(token, Jpeg(400, 300));
        var linked = await SeriesPhotoAsync(token, tank);
        var last = await LibraryPhotoAsync(token, Jpeg(401, 300));

        var all = await ListAsync(token);
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal([last, linked, unlinked], IdsOf(all));
        Assert.Equal([last, unlinked], IdsOf(await ListAsync(token, "?filter=unlinked")));
        Assert.Equal([linked], IdsOf(await ListAsync(token, "?filter=linked")));
        Assert.Equal(3, (await ListAsync(token, "?kind=photo")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await ListAsync(token, "?kind=video")).GetProperty("total").GetInt32());
        var page = await ListAsync(token, "?limit=2&offset=1");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal([linked, unlinked], IdsOf(page));
        var linkedItem = all.GetProperty("items")[1];
        Assert.Equal(1, linkedItem.GetProperty("placeCount").GetInt32());
        Assert.Equal(["tank"], linkedItem.GetProperty("placeKinds").EnumerateArray().Select(k => k.GetString()));

        using var badFilter = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media?filter=everything", token);
        await AssertProblemAsync(badFilter, HttpStatusCode.BadRequest, "library_filter_invalid");
        using var badKind = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/media?kind=audio", token);
        await AssertProblemAsync(badKind, HttpStatusCode.BadRequest, "library_filter_invalid");
    }

    [Fact]
    public async Task GivenACareEntryWithAPhoto_WhenTheEntryIsDeleted_ThenThePhotoIsStillInTheLibrary()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var entry = await LogAsync(fixture.Client, token, tank);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(650, 450));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var add = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        add.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var added = await fixture.Client.SendAsync(add);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var inLibrary = IdsOf(await ListAsync(token));
        var id = Assert.Single(inLibrary);
        Assert.Equal(1, (await ListAsync(token, "?filter=linked")).GetProperty("total").GetInt32());

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Equal([id], IdsOf(await ListAsync(token)));
        Assert.Equal([id], IdsOf(await ListAsync(token, "?filter=unlinked")));
    }

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private async Task<JsonElement> DetailAsync(string token, long id)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}?date={Today}&utcOffsetMinutes=0", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    [Fact]
    public async Task GivenTheOnlyAppPhotoOfAWeekOfTheTimeLapse_WhenTheOwnerOpensTheDetail_ThenItNamesTheTankAndTheWeekItKeeps()
    {
        var token = NewMemberToken();
        var title = $"Streak tank {Guid.NewGuid():N}"[..22];
        var tank = await TankWithTitleAsync(token, title);
        var only = await SeriesPhotoAsync(token, tank, capturedInApp: true);

        var kept = Assert.Single((await DetailAsync(token, only)).GetProperty("keepsStreak").EnumerateArray());
        Assert.Equal(tank, kept.GetProperty("tankId").GetInt64());
        Assert.Equal(title, kept.GetProperty("tankTitle").GetString());
        var monday = DateOnly.Parse(kept.GetProperty("weekStart").GetString()!);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.InRange(DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - monday.DayNumber, 0, 6);

        var second = await SeriesPhotoAsync(token, tank, capturedInApp: true, width: 710);
        Assert.Empty((await DetailAsync(token, only)).GetProperty("keepsStreak").EnumerateArray());
        Assert.Empty((await DetailAsync(token, second)).GetProperty("keepsStreak").EnumerateArray());
    }

    [Fact]
    public async Task GivenAPhotoThatKeepsNoStreakWeek_ThenTheDetailReportsNone()
    {
        var token = NewMemberToken();
        var tank = await TankWithTitleAsync(token, $"Plain tank {Guid.NewGuid():N}"[..22]);
        var notInApp = await SeriesPhotoAsync(token, tank);
        var unlinked = await LibraryPhotoAsync(token, Jpeg(444, 300));
        Assert.Empty((await DetailAsync(token, notInApp)).GetProperty("keepsStreak").EnumerateArray());
        Assert.Empty((await DetailAsync(token, unlinked)).GetProperty("keepsStreak").EnumerateArray());
        using var badDate = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{unlinked}?date=nope", token);
        await AssertProblemAsync(badDate, HttpStatusCode.BadRequest, "streak_date_invalid");
    }

    [Fact]
    public async Task GivenAMemberWhoSwitchedTheStreaksOff_ThenTheDetailReportsNoKeptWeek()
    {
        var token = NewMemberToken();
        var tank = await TankWithTitleAsync(token, $"Off tank {Guid.NewGuid():N}"[..22]);
        var only = await SeriesPhotoAsync(token, tank, capturedInApp: true);
        using var off = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/settings", token, new { streaksEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Empty((await DetailAsync(token, only)).GetProperty("keepsStreak").EnumerateArray());
    }

    [Fact]
    public async Task GivenAPhotoInATankSectionAndInAStory_WhenTheOwnerOpensTheDetail_ThenTheNamesOfBothPlacesAreListed()
    {
        var token = NewMemberToken();
        var title = $"Library tank {Guid.NewGuid():N}"[..24];
        var tank = await TankWithTitleAsync(token, title);
        var item = await SeriesPhotoAsync(token, tank);
        var story = await PublishedStoryWithAsync(token, "Library story", item);

        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{item}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await BodyAsync(response);
        Assert.True(detail.GetProperty("canDelete").GetBoolean());
        var places = detail.GetProperty("places").EnumerateArray().ToList();
        Assert.Equal(2, places.Count);
        var tankPlace = places.Single(p => p.GetProperty("kind").GetString() == "tank");
        Assert.Equal(title, tankPlace.GetProperty("name").GetString());
        Assert.Equal(tank, tankPlace.GetProperty("tankId").GetInt64());
        Assert.Equal("time_lapse", tankPlace.GetProperty("section").GetString());
        var storyPlace = places.Single(p => p.GetProperty("kind").GetString() == "story");
        Assert.Equal("Library story", storyPlace.GetProperty("name").GetString());
        Assert.Equal(story, storyPlace.GetProperty("storyId").GetInt64());
        Assert.False(string.IsNullOrEmpty(storyPlace.GetProperty("storySlug").GetString()));
        Assert.Equal(2, detail.GetProperty("item").GetProperty("placeCount").GetInt32());
    }

    [Fact]
    public async Task GivenAPhotoInATankSectionAndInAStory_WhenTheOwnerDeletesItFromTheLibrary_ThenItDisappearsFromBothAndItsFilesAreRemoved()
    {
        var token = NewMemberToken();
        var tank = await TankWithTitleAsync(token, $"Delete tank {Guid.NewGuid():N}"[..24]);
        var item = await SeriesPhotoAsync(token, tank);
        var keepsItems = await LibraryPhotoAsync(token, Jpeg(500, 350));
        var multiStory = await PublishedStoryWithAsync(token, "Two items", item, keepsItems);
        var singleStory = await PublishedStoryWithAsync(token, "One item", item);
        List<string> keys;
        await using (var db = fixture.CreateDbContext())
        {
            var stored = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == item);
            keys = [.. stored.Variants.Select(v => v.StorageKey).Append(stored.StorageKey)];
        }

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{item}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var removed = (await BodyAsync(deleted)).GetProperty("places").EnumerateArray().Select(p => p.GetProperty("kind").GetString()).Order().ToList();
        Assert.Equal(["story", "story", "tank"], removed);

        await using var after = fixture.CreateDbContext();
        Assert.False(await after.MediaItems.AnyAsync(m => m.Id == item));
        Assert.False(await after.MediaLinks.AnyAsync(l => l.MediaItemId == item));
        Assert.False(await after.MediaVariants.AnyAsync(v => v.MediaItemId == item));
        foreach (var key in keys) Assert.False(await fixture.ObjectStore.ExistsAsync(key), key);
        Assert.Equal([keepsItems], await after.MediaLinks.Where(l => l.StoryId == multiStory).Select(l => l.MediaItemId).ToListAsync());
        Assert.Null((await after.Stories.SingleAsync(s => s.Id == multiStory)).DeletedAt);
        var emptied = await after.Stories.SingleAsync(s => s.Id == singleStory);
        Assert.NotNull(emptied.DeletedAt);
        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", token);
        Assert.Equal(0, (await BodyAsync(series)).GetProperty("photos").GetArrayLength());
        using var again = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{item}", token);
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "media_not_found");
    }

    [Fact]
    public async Task GivenATankVideo_WhenListingAndDeleting_ThenTheLibraryShowsItWithItsPosterAndDeletingRemovesAllItsObjects()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var mediaItemId = (await UploadedAsync(fixture.Client, token, await SmallVideo, tank)).GetProperty("id").GetInt64();
        await ProcessQueueAsync(fixture.Services);
        var photo = await LibraryPhotoAsync(token, Jpeg(480, 360));

        var videos = await ListAsync(token, "?kind=video");
        Assert.Equal([mediaItemId], IdsOf(videos));
        var entry = videos.GetProperty("items")[0];
        Assert.Equal("video", entry.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("image").ValueKind);
        var video = entry.GetProperty("video");
        Assert.Equal("ready", video.GetProperty("state").GetString());
        var posterUrl = video.GetProperty("posterUrl").GetString();
        Assert.False(string.IsNullOrEmpty(posterUrl));
        Assert.Equal(["tank"], entry.GetProperty("placeKinds").EnumerateArray().Select(k => k.GetString()));
        Assert.Equal([photo], IdsOf(await ListAsync(token, "?kind=photo")));

        List<string> keys;
        await using (var db = fixture.CreateDbContext())
        {
            var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.Id == mediaItemId);
            keys = [item.StorageKey, .. item.Variants.Select(v => v.StorageKey)];
        }

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{mediaItemId}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        foreach (var key in keys) Assert.False(await fixture.ObjectStore.ExistsAsync(key), key);
        await using var after = fixture.CreateDbContext();
        Assert.False(await after.MediaItems.AnyAsync(m => m.Id == mediaItemId));
        Assert.False(await after.VideoJobs.AnyAsync(j => j.MediaItemId == mediaItemId));
        using var tankVideos = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        Assert.Equal(0, (await BodyAsync(tankVideos)).GetArrayLength());
    }

    [Fact]
    public async Task GivenAFileInTheLibrary_WhenTheOwnerDeletesIt_ThenTheSignedLinkStopsWorkingAndTheListForgetsIt()
    {
        var token = NewMemberToken();
        using var upload = await UploadToLibraryAsync(fixture.Client, token, Jpeg(900, 600));
        var item = await BodyAsync(upload);
        var id = item.GetProperty("id").GetInt64();
        var thumb = item.GetProperty("image").GetProperty("thumb").GetString()!;
        using (var before = await fixture.Client.GetAsync(thumb)) Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var gone = await fixture.Client.GetAsync(thumb);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "media_not_found");
        Assert.DoesNotContain(id, IdsOf(await ListAsync(token)));
    }

    [Fact]
    public async Task GivenAPhotoThatProvedADecommissioning_WhenTheOwnerDeletesItFromTheLibrary_ThenItIsRefusedAndStays()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(660, 480));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        content.Add(new StringContent("true"), "capturedInApp");
        content.Add(new StringContent("all_care"), "scope");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/tanks/{tank}/decommissioning?date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&utcOffsetMinutes=0") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var started = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = Assert.Single(IdsOf(await ListAsync(token)));

        using var detail = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/media/{id}", token);
        Assert.False((await BodyAsync(detail)).GetProperty("canDelete").GetBoolean());
        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", token);
        await AssertProblemAsync(deleted, HttpStatusCode.Conflict, "media_is_proof");
        Assert.Equal([id], IdsOf(await ListAsync(token)));
    }

    [Fact]
    public async Task GivenAFinishedWaterChangePhoto_WhenItIsDeletedFromTheLibrary_ThenTheSamePictureStillCountsAsUsedBefore()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var bytes = Jpeg(670, 480);
        Assert.Equal("verified", await FinishWaterChangeWithAsync(token, tank, bytes));
        var ids = IdsOf(await ListAsync(token));
        var id = Assert.Single(ids);

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/media/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Equal("partial", await FinishWaterChangeWithAsync(token, tank, bytes));
    }

    private async Task<string> FinishWaterChangeWithAsync(string token, long tank, byte[] bytes)
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
