using System.Net;
using System.Net.Http.Headers;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Uploads;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;
using static Cichlids.Api.Tests.Features.Videos.VideoTestSupport;

namespace Cichlids.Api.Tests.Features.Media;

/// <summary>
/// One visibility rule decides who gets a signed link and which area holds the files: other members,
/// anonymous viewers, entries set to only me, drafts, removed posts, expired links and deleted media items.
/// </summary>
[Collection(UploadsCollection.Name)]
public sealed class MediaDeliverySecurityTests(UploadsFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(new Dictionary<string, string?> { ["MediaPlacement:ImmediateBudget"] = "00:00:00" }, null);
        client = _host.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await _host.DisposeAsync();
    }

    private async Task PlaceAsync(params long[] mediaItemIds)
    {
        while (true)
        {
            using var scope = _host.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<MediaPlacementService>().RunOnceAsync(CancellationToken.None, mediaItemIds)) return;
        }
    }

    private async Task<bool> PubliclyReadableAsync(string key)
    {
        using var anonymous = new HttpClient();
        using var response = await anonymous.GetAsync($"{fixture.PublicBaseUrl}/{key}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<(long Entry, long ItemId, string Thumb, List<string> Keys)> CarePhotoAsync(string token, long tank)
    {
        var entry = await LogAsync(client, token, tank);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(800, 600));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var thumb = (await BodyAsync(response)).GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
        await using var db = fixture.CreateDbContext();
        var itemId = await db.MediaLinks.Where(l => l.CareEntryId == entry).Select(l => l.MediaItemId).SingleAsync();
        var keys = await db.MediaVariants.Where(v => v.MediaItemId == itemId).Select(v => v.StorageKey).ToListAsync();
        return (entry, itemId, thumb, keys);
    }

    [Fact]
    public async Task GivenAPhotoOfAPrivateTankInAPublishedStory_WhenAnonymousViewersOpenTheStory_ThenThePhotoLoadsFromThePublicAreaAndTheTankStaysHidden()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        using var added = await AddSeriesPhotoAsync(token, tank);
        var itemId = (await BodyAsync(added)).GetProperty("mediaItemId").GetInt64();
        var draft = await UploadDraftAsync(client, token, Jpeg(300, 200));
        await using (var db = fixture.CreateDbContext())
        {
            var postItem = await db.MediaLinks.Where(l => l.PostId == draft.Id).SingleAsync();
            db.MediaLinks.Remove(postItem);
            db.MediaLinks.Add(MediaLink.ForPost(draft.Id, itemId));
            await db.SaveChangesAsync();
        }

        using var publish = await PublishAsync(client, token, draft.Id, new PublishPostRequest("Story", null, "cichlids"));
        publish.EnsureSuccessStatusCode();
        await PlaceAsync(itemId);

        var detail = await client.GetAsync($"/api/pictures/{(await ReadAsync<PictureDetailDto>(publish)).Slug}");
        detail.EnsureSuccessStatusCode();
        var picture = await ReadAsync<PictureDetailDto>(detail);
        Assert.StartsWith($"{fixture.PublicBaseUrl}/variants/", picture.Image!.Thumb);
        using var anonymousTank = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}", null);
        await AssertProblemAsync(anonymousTank, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymousSeries = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/time-lapse", null);
        await AssertProblemAsync(anonymousSeries, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Fact]
    public async Task GivenACareEntryOfAPublicHistory_WhenItIsSetToOnlyMe_ThenThePhotoMovesToThePrivateAreaAndOnlyTheOwnerGetsASignedLink()
    {
        var (token, tank) = await OwnTankAsync(client);
        var (entry, itemId, thumb, _) = await CarePhotoAsync(token, tank);
        await PlaceAsync(itemId);
        var publicKeys = (await VariantKeysAsync(itemId)).ToList();
        Assert.All(publicKeys, key => Assert.StartsWith("variants/", key));
        Assert.True(await PubliclyReadableAsync(publicKeys[0]));

        using var patch = await SendAsync(client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{entry}", token, new { onlyMe = true });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        await PlaceAsync(itemId);

        Assert.All(await VariantKeysAsync(itemId), key => Assert.StartsWith("private/variants/", key));
        foreach (var key in publicKeys) Assert.False(await PubliclyReadableAsync(key));
        var other = NewMemberToken();
        foreach (var viewer in new string?[] { null, other })
        {
            using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", viewer);
            Assert.DoesNotContain("/api/media/photos/", await list.Content.ReadAsStringAsync());
        }

        using var own = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var ownThumb = (await BodyAsync(own)).GetProperty("items")[0].GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
        using var served = await client.GetAsync(ownThumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.NotNull(thumb);
    }

    [Fact]
    public async Task GivenAPublicHistoryEntryThatIsNotOnlyMe_ThenOtherViewersGetSignedLinksToItsPhoto()
    {
        var (token, tank) = await OwnTankAsync(client);
        var (_, itemId, _, _) = await CarePhotoAsync(token, tank);
        await PlaceAsync(itemId);

        foreach (var viewer in new string?[] { null, NewMemberToken() })
        {
            using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", viewer);
            var url = (await BodyAsync(list)).GetProperty("items")[0].GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
            using var served = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        }
    }

    [Fact]
    public async Task GivenADraft_ThenItsFilesAreNotPublicAndNoOtherMemberOrAnonymousViewerGetsALinkToThem()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(client, token, Jpeg(400, 300));
        await using var db = fixture.CreateDbContext();
        var itemId = await db.MediaLinks.Where(l => l.PostId == draft.Id).Select(l => l.MediaItemId).SingleAsync();

        foreach (var key in await VariantKeysAsync(itemId)) Assert.False(await PubliclyReadableAsync(key));
        using var others = await ListDraftsAsync(client, NewToken());
        Assert.DoesNotContain("/api/media/photos/", await others.Content.ReadAsStringAsync());
        using var anonymous = await ListDraftsAsync(client, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var anonymousDetail = await client.GetAsync($"/api/pictures/{draft.Id}");
        Assert.Equal(HttpStatusCode.NotFound, anonymousDetail.StatusCode);
    }

    [Fact]
    public async Task GivenAPublishedPost_WhenItIsRemoved_ThenItsFilesMoveToThePrivateArea()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(client, token, Jpeg(400, 300));
        using var publish = await PublishAsync(client, token, draft.Id, new PublishPostRequest("Gone", null, "cichlids"));
        publish.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var itemId = await db.MediaLinks.Where(l => l.PostId == draft.Id).Select(l => l.MediaItemId).SingleAsync();
        await PlaceAsync(itemId);
        var publicKeys = await VariantKeysAsync(itemId);
        Assert.All(publicKeys, key => Assert.StartsWith("variants/", key));

        await db.Posts.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.State, PostState.Archived));
        using (var scope = _host.Services.CreateScope())
        {
            var placement = scope.ServiceProvider.GetRequiredService<MediaPlacementService>();
            Assert.True(await placement.ReconcileAsync(CancellationToken.None) >= 1);
        }

        await PlaceAsync(itemId);
        Assert.All(await VariantKeysAsync(itemId), key => Assert.StartsWith("private/variants/", key));
        foreach (var key in publicKeys) Assert.False(await PubliclyReadableAsync(key));
    }

    [Fact]
    public async Task GivenASignedLink_WhenItExpiresOrTheMediaItemIsDeleted_ThenTheRouteRefusesIt()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        var (_, itemId, thumb, _) = await CarePhotoAsync(token, tank);
        using (var served = await client.GetAsync(thumb)) Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        var protector = _host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(MediaUrlSigner.PhotoPurpose).ToTimeLimitedDataProtector();
        await using var db = fixture.CreateDbContext();
        var variantId = await db.MediaVariants.Where(v => v.MediaItemId == itemId).Select(v => v.Id).FirstAsync();
        var expired = protector.Protect(variantId.ToString(), DateTimeOffset.UtcNow.AddMinutes(-1));
        using var late = await client.GetAsync($"/api/media/photos/{expired}");
        await AssertProblemAsync(late, HttpStatusCode.NotFound, "media_not_found");

        await db.MediaItems.Where(m => m.Id == itemId).ExecuteDeleteAsync();
        using var deleted = await client.GetAsync(thumb);
        await AssertProblemAsync(deleted, HttpStatusCode.NotFound, "media_not_found");
    }

    private async Task<string> OwnCareThumbAsync(string token, long tank)
    {
        using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        return (await BodyAsync(list)).GetProperty("items")[0].GetProperty("photos")[0].GetProperty("urls").GetProperty("thumb").GetString()!;
    }

    private async Task AssertServedAsync(string url, HttpStatusCode expected)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task GivenALinkIssuedToTheOwner_WhenTheTankIsMadePrivate_ThenTheSameLinkIsRefusedAndANewLinkWorks()
    {
        var (token, tank) = await OwnTankAsync(client);
        await CarePhotoAsync(token, tank);
        var link = await OwnCareThumbAsync(token, tank);
        await AssertServedAsync(link, HttpStatusCode.OK);

        using var response = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = "private", stream = "private" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertServedAsync(link, HttpStatusCode.NotFound);
        await AssertServedAsync(await OwnCareThumbAsync(token, tank), HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenALinkIssuedToTheOwner_WhenTheEntryIsToggledToOnlyMe_ThenTheSameLinkIsRefusedAndANewLinkWorks()
    {
        var (token, tank) = await OwnTankAsync(client);
        var (entry, _, _, _) = await CarePhotoAsync(token, tank);
        var link = await OwnCareThumbAsync(token, tank);

        using var patch = await SendAsync(client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{entry}", token, new { onlyMe = true });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        await AssertServedAsync(link, HttpStatusCode.NotFound);
        await AssertServedAsync(await OwnCareThumbAsync(token, tank), HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenALinkToAPhotoShownByAStory_WhenTheStoryIsWithdrawnOrItsLinkIsRemoved_ThenTheSameLinkIsRefusedAndANewLinkWorks()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        var (_, itemId, _, _) = await CarePhotoAsync(token, tank);
        var draft = await UploadDraftAsync(client, token, Jpeg(300, 200));
        long linkId;
        await using (var db = fixture.CreateDbContext())
        {
            var other = await db.MediaLinks.Where(l => l.PostId == draft.Id).SingleAsync();
            db.MediaLinks.Remove(other);
            await db.SaveChangesAsync();
            var shown = MediaLink.ForPost(draft.Id, itemId);
            db.MediaLinks.Add(shown);
            await db.SaveChangesAsync();
            linkId = shown.Id;
        }

        var first = await OwnCareThumbAsync(token, tank);
        await AssertServedAsync(first, HttpStatusCode.OK);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Posts.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.State, PostState.Archived));
            await db.MarkForPlacementAsync(db.MediaItemIdsOfPost(draft.Id));
        }

        await AssertServedAsync(first, HttpStatusCode.NotFound);
        var second = await OwnCareThumbAsync(token, tank);
        await AssertServedAsync(second, HttpStatusCode.OK);
        await using (var db = fixture.CreateDbContext())
        {
            db.MediaLinks.Remove(await db.MediaLinks.SingleAsync(l => l.Id == linkId));
            await db.SaveChangesAsync();
        }

        await AssertServedAsync(second, HttpStatusCode.NotFound);
        await AssertServedAsync(await OwnCareThumbAsync(token, tank), HttpStatusCode.OK);
    }

    [Fact]
    public async Task GivenAPlaylistLink_WhenTheVisibilityChanges_ThenThePlaylistAndEverySegmentUnderTheTokenAreRefused()
    {
        var (token, tank) = await OwnTankAsync(client, isPublic: false);
        await UploadedAsync(client, token, await SmallVideo, tank);
        await ProcessQueueAsync(_host.Services);
        using var list = await SendAsync(client, HttpMethod.Get, $"/api/tanks/{tank}/videos", token);
        var playlist = (await BodyAsync(list))[0].GetProperty("playlistUrl").GetString()!;
        var baseUrl = playlist[..^"master.m3u8".Length];
        var masterText = await client.GetStringAsync(playlist);
        var rendition = masterText.Split('\n').Single(l => l.EndsWith("index.m3u8", StringComparison.Ordinal));
        await AssertServedAsync(baseUrl + rendition, HttpStatusCode.OK);

        using var response = await SendAsync(client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, new { profile = "public", stream = "public" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertServedAsync(playlist, HttpStatusCode.NotFound);
        await AssertServedAsync(baseUrl + rendition, HttpStatusCode.NotFound);
    }

    private async Task<List<string>> VariantKeysAsync(long itemId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.MediaVariants.Where(v => v.MediaItemId == itemId).Select(v => v.StorageKey).ToListAsync();
    }

    private async Task<HttpResponseMessage> AddSeriesPhotoAsync(string token, long tank)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(800, 600));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
