using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Tests.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(UploadsCollection.Name)]
public sealed class CarePhotoTests(UploadsFixture fixture)
{
    [Fact]
    public async Task GivenOwnEntry_WhenAddingAPhoto_ThenEntryListsItAndOnlyPrivateKeysAreStored()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var response = await AddPhotoAsync(token, tank, id, Jpeg(1700, 1000));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await BodyAsync(response);
        var photo = Assert.Single(entry.GetProperty("photos").EnumerateArray());
        Assert.Equal(1700, photo.GetProperty("width").GetInt32());
        Assert.Equal(1000, photo.GetProperty("height").GetInt32());
        var urls = photo.GetProperty("urls");
        foreach (var label in new[] { "thumb", "small", "medium", "large" })
            Assert.StartsWith("/api/care-photos/", urls.GetProperty(label).GetString());
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Single((await BodyAsync(list)).GetProperty("items")[0].GetProperty("photos").EnumerateArray());

        var profileId = entry.GetProperty("authorProfileId").GetInt64();
        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.OwnerProfileId == profileId);
        Assert.StartsWith("private/", item.StorageKey);
        Assert.Equal(4, item.Variants.Count);
        Assert.All(item.Variants, v => Assert.StartsWith("private/", v.StorageKey));
        Assert.True(await fixture.ObjectStore.ExistsAsync(item.StorageKey));
    }

    [Fact]
    public async Task GivenPhotoWithExifDate_WhenAddingItToAnEntry_ThenTheMediaItemStoresTheCaptureTime()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var response = await AddPhotoAsync(token, tank, id, PhotoFixtures.PhoneJpeg(1200, 800));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var profileId = (await BodyAsync(response)).GetProperty("authorProfileId").GetInt64();

        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.SingleAsync(m => m.OwnerProfileId == profileId);
        Assert.Equal(new DateTime(2024, 5, 1, 10, 20, 30), item.TakenAtLocal);
        Assert.Equal((short)120, item.TakenAtOffsetMinutes);
    }

    [Fact]
    public async Task GivenCarePhoto_WhenRequestingObjectsDirectly_ThenTheStoreRefusesAndTheSignedUrlServesTheVariant()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var added = await AddPhotoAsync(token, tank, id, Jpeg(1200, 800));
        var entry = await BodyAsync(added);
        var profileId = entry.GetProperty("authorProfileId").GetInt64();
        await using var db = fixture.CreateDbContext();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.OwnerProfileId == profileId);
        using var anonymous = new HttpClient();
        foreach (var key in item.Variants.Select(v => v.StorageKey).Append(item.StorageKey))
        {
            using var direct = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
            Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        }
        var thumb = entry.GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString();
        using var served = await fixture.Client.GetAsync(thumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", served.Headers.CacheControl?.ToString());
        Assert.True((await served.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]));
    }

    [Fact]
    public async Task GivenOtherMemberOrAnonymous_WhenTouchingPhotos_ThenCodedRefusals()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, owner, tank);
        using var added = await AddPhotoAsync(owner, tank, id, Jpeg(400, 300));
        var photoId = (await BodyAsync(added)).GetProperty("photos")[0].GetProperty("id").GetInt64();
        var other = NewMemberToken();
        using var otherAdd = await AddPhotoAsync(other, tank, id, Jpeg(400, 300));
        await AssertProblemAsync(otherAdd, HttpStatusCode.NotFound, "care_not_found");
        using var otherRemove = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}/photos/{photoId}", other);
        await AssertProblemAsync(otherRemove, HttpStatusCode.NotFound, "care_not_found");
        using var anonymousAdd = await AddPhotoAsync(null, tank, id, Jpeg(400, 300));
        await AssertProblemAsync(anonymousAdd, HttpStatusCode.Unauthorized, "care_auth_required");
        using var otherList = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", other);
        await AssertProblemAsync(otherList, HttpStatusCode.NotFound, "care_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, (await EventsAsync(db, "care", id)).Count);
    }

    [Fact]
    public async Task GivenTamperedExpiredOrForeignToken_WhenRequestingAPhoto_ThenCodedNotFound()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var added = await AddPhotoAsync(token, tank, id, Jpeg(400, 300));
        var url = (await BodyAsync(added)).GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
        using var tampered = await fixture.Client.GetAsync(url[..^4] + "AAAA");
        await AssertProblemAsync(tampered, HttpStatusCode.NotFound, "care_photo_not_found");
        using var garbage = await fixture.Client.GetAsync("/api/care-photos/not-a-token");
        await AssertProblemAsync(garbage, HttpStatusCode.NotFound, "care_photo_not_found");

        var protector = fixture.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Cichlids.CarePhotoUrl").ToTimeLimitedDataProtector();
        await using var db = fixture.CreateDbContext();
        var variantId = await db.MediaVariants.Where(v => v.StorageKey.StartsWith("private/")).OrderByDescending(v => v.Id).Select(v => v.Id).FirstAsync();
        var expired = protector.Protect(variantId.ToString(), DateTimeOffset.UtcNow.AddMinutes(-1));
        using var late = await fixture.Client.GetAsync($"/api/care-photos/{expired}");
        await AssertProblemAsync(late, HttpStatusCode.NotFound, "care_photo_not_found");

        var (publicToken, _) = NewCaller();
        using var draft = await UploadAsync(fixture.Client, publicToken, Jpeg(300, 200), "image/jpeg");
        var publicVariant = await db.MediaVariants.Where(v => v.StorageKey.StartsWith("variants/")).OrderByDescending(v => v.Id).Select(v => v.Id).FirstAsync();
        var foreign = protector.Protect(publicVariant.ToString(), TimeSpan.FromMinutes(5));
        using var notCare = await fixture.Client.GetAsync($"/api/care-photos/{foreign}");
        await AssertProblemAsync(notCare, HttpStatusCode.NotFound, "care_photo_not_found");
    }

    [Fact]
    public async Task GivenSixPhotos_WhenAddingASeventh_ThenConflictAndNothingIsStored()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        long profileId = 0;
        for (var i = 0; i < 6; i++)
        {
            using var ok = await AddPhotoAsync(token, tank, id, Jpeg(300 + i, 200));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            profileId = (await BodyAsync(ok)).GetProperty("authorProfileId").GetInt64();
        }
        await using var db = fixture.CreateDbContext();
        var before = await db.MediaItems.CountAsync(m => m.OwnerProfileId == profileId);
        var keysBefore = await CountKeysAsync(profileId);
        using var seventh = await AddPhotoAsync(token, tank, id, Jpeg(400, 300));
        await AssertProblemAsync(seventh, HttpStatusCode.Conflict, "care_photo_limit_reached");
        Assert.Equal(before, await db.MediaItems.CountAsync(m => m.OwnerProfileId == profileId));
        Assert.Equal(keysBefore, await CountKeysAsync(profileId));
        Assert.Equal(7, (await EventsAsync(db, "care", id)).Count);
    }

    [Fact]
    public async Task GivenSixPhotos_WhenRemovingOneAndAddingAnother_ThenTheSlotIsReused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        JsonElement entry = default;
        for (var i = 0; i < 6; i++)
        {
            using var ok = await AddPhotoAsync(token, tank, id, Jpeg(300 + i, 200));
            entry = await BodyAsync(ok);
        }
        var first = entry.GetProperty("photos")[2].GetProperty("id").GetInt64();
        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}/photos/{first}", token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(5, (await BodyAsync(removed)).GetProperty("photos").GetArrayLength());
        using var again = await AddPhotoAsync(token, tank, id, Jpeg(500, 200));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(6, (await BodyAsync(again)).GetProperty("photos").GetArrayLength());
    }

    [Fact]
    public async Task GivenPhoto_WhenRemovedOrTheEntryIsDeleted_ThenRowsObjectsAndUrlsAreGoneAndEventsFollow()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var first = await AddPhotoAsync(token, tank, id, Jpeg(600, 400));
        using var second = await AddPhotoAsync(token, tank, id, Png(600, 400), "image/png");
        var entry = await BodyAsync(second);
        var profileId = entry.GetProperty("authorProfileId").GetInt64();
        var photos = entry.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        await using var db = fixture.CreateDbContext();
        var keys = (await db.MediaItems.Include(m => m.Variants).Where(m => m.OwnerProfileId == profileId).ToListAsync())
            .SelectMany(m => m.Variants.Select(v => v.StorageKey).Append(m.StorageKey)).ToList();
        Assert.Equal(6, keys.Count);

        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}/photos/{photos[0].GetProperty("id").GetInt64()}", token);
        Assert.Equal(1, (await BodyAsync(removed)).GetProperty("photos").GetArrayLength());
        Assert.Equal(1, await db.MediaItems.CountAsync(m => m.OwnerProfileId == profileId));
        using var gone = await fixture.Client.GetAsync(photos[0].GetProperty("urls").GetProperty("thumb").GetString());
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "care_photo_not_found");
        using var missing = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}/photos/{photos[0].GetProperty("id").GetInt64()}", token);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "care_not_found");

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(0, await db.MediaItems.CountAsync(m => m.OwnerProfileId == profileId));
        Assert.Equal(0, await CountPhotoRowsAsync(id));
        foreach (var key in keys) Assert.False(await fixture.ObjectStore.ExistsAsync(key), key);

        var events = await EventsAsync(db, "care", id);
        Assert.Equal(new[] { "care.logged", "care.updated", "care.updated", "care.updated", "care.deleted" }, events.Select(e => e.Type));
        Assert.Equal(new[] { 1, 2, 1 }, events.Skip(1).Take(3).Select(e => e.Payload.GetProperty("photos").GetArrayLength()));
        Assert.Equal(1, events[4].Payload.GetProperty("photos").GetArrayLength());
    }

    [Theory]
    [InlineData("image/jpeg", "text")]
    [InlineData("text/plain", "jpeg")]
    [InlineData("image/jpeg", "undecodable")]
    [InlineData("image/jpeg", "empty")]
    public async Task GivenUnusableFile_WhenAddingAPhoto_ThenCodedBadRequestAndNothingStored(string contentType, string kind)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        var bytes = kind switch
        {
            "jpeg" => Jpeg(300, 200),
            "undecodable" => UndecodableJpeg(),
            "empty" => [],
            _ => "plain text"u8.ToArray(),
        };
        using var response = await AddPhotoAsync(token, tank, id, bytes, contentType);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "care_photo_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.Single(await EventsAsync(db, "care", id));
    }

    [Fact]
    public async Task GivenWebpPhoto_WhenAdding_ThenItIsAccepted()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var response = await AddPhotoAsync(token, tank, id, Webp(500, 300), "image/webp");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GivenHourlyUploadLimit_WhenAddingPastIt_ThenRateLimitedProblem()
    {
        var (token, tank) = await OwnTankAsync(fixture.ClientWithTwoUploadsPerHour);
        var id = await LogAsync(fixture.ClientWithTwoUploadsPerHour, token, tank);
        for (var i = 0; i < 2; i++)
        {
            using var ok = await AddPhotoAsync(token, tank, id, Jpeg(300 + i, 200), client: fixture.ClientWithTwoUploadsPerHour);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        using var third = await AddPhotoAsync(token, tank, id, Jpeg(400, 300), client: fixture.ClientWithTwoUploadsPerHour);
        await AssertProblemAsync(third, HttpStatusCode.TooManyRequests, "upload_rate_limited");
    }

    private async Task<int> CountKeysAsync(long profileId)
    {
        var count = 0;
        await foreach (var _ in fixture.ObjectStore.ListKeysAsync($"private/care/{profileId}/")) count++;
        return count;
    }

    private async Task<int> CountPhotoRowsAsync(long entryId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM care_photo WHERE care_entry_id = {entryId}").SingleAsync();
    }

    private async Task<HttpResponseMessage> AddPhotoAsync(string? token, long tank, long entry, byte[] bytes,
        string contentType = "image/jpeg", HttpClient? client = null)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await (client ?? fixture.Client).SendAsync(request);
    }
}
