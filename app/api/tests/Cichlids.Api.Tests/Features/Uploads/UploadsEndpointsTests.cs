using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Uploads;

[Collection(UploadsCollection.Name)]
public class UploadsEndpointsTests(UploadsFixture fixture)
{
    [Fact]
    public async Task Upload_AnonymousReturnsUnauthorized()
    {
        var response = await UploadAsync(fixture.Client, token: null, Jpeg(300, 200), "image/jpeg");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public async Task Upload_UnsupportedContentTypeReturnsBadRequest(string contentType)
    {
        var response = await UploadAsync(fixture.Client, NewToken(), Jpeg(300, 200), contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be image/jpeg", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_ContentThatDoesNotMatchTheDeclaredTypeReturnsBadRequest()
    {
        var response = await UploadAsync(fixture.Client, NewToken(), Png(300, 200), "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("content is not image/jpeg", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_BytesThatAreNoImageReturnBadRequest()
    {
        var response = await UploadAsync(fixture.Client, NewToken(), "not a real jpeg"u8.ToArray(), "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("content is not image/jpeg", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_UndecodableJpegReturnsBadRequestAndStoresNothing()
    {
        var (token, subject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, token)).EnsureSuccessStatusCode();

        var response = await UploadAsync(fixture.Client, token, UndecodableJpeg(), "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("could not be decoded", await response.Content.ReadAsStringAsync());

        var profileId = await ResolveProfileIdAsync(subject);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaItems.AnyAsync(m => m.OwnerProfileId == profileId));
        Assert.False(await db.Posts.AnyAsync(p => p.AuthorProfileId == profileId));
        Assert.Empty(await ListStoredKeysAsync($"originals/uploads/{profileId}/"));
    }

    [Fact]
    public async Task Upload_FileAbove25MegabytesReturnsBadRequest()
    {
        var oversized = new byte[(25 * 1024 * 1024) + 1];
        Jpeg(300, 200).CopyTo(oversized, 0);

        var response = await UploadAsync(fixture.Client, NewToken(), oversized, "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("exceeds the limit", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_ImageAbove100MegapixelsReturnsBadRequestAndStoresNothing()
    {
        var (token, subject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, token)).EnsureSuccessStatusCode();
        var bytes = FlatJpeg(12000, 9000);
        Assert.True(bytes.Length < UploadsEndpoints.MaxFileBytes);

        var response = await UploadAsync(fixture.Client, token, bytes, "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("100 megapixels", await response.Content.ReadAsStringAsync());

        var profileId = await ResolveProfileIdAsync(subject);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MediaItems.AnyAsync(m => m.OwnerProfileId == profileId));
        Assert.Empty(await ListStoredKeysAsync($"originals/uploads/{profileId}/"));
        Assert.Empty(await ListStoredKeysAsync("variants/thumb/originals/uploads/" + profileId + "/"));
    }

    [Fact]
    public async Task Upload_DocumentsTheRequestBodyLimitResponseInTheOpenApiDocument()
    {
        var response = await fixture.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/uploads").GetProperty("post").GetProperty("responses");
        Assert.True(responses.TryGetProperty("413", out _), "The upload operation does not document a 413 response.");
    }

    [Fact]
    public async Task UploadAndDiscard_WorkWithoutASlugSecret()
    {
        var token = NewToken();

        var draft = await UploadDraftAsync(fixture.ClientWithoutSlugSecret, token, Jpeg(300, 200));
        var discard = await DiscardAsync(fixture.ClientWithoutSlugSecret, token, draft.Id);

        Assert.Equal(HttpStatusCode.NoContent, discard.StatusCode);
    }

    [Fact]
    public async Task Upload_JpegCreatesDraftWithMediaItemVariantsAndStoredObjects()
    {
        var (token, subject) = NewCaller();
        var bytes = Jpeg(1000, 700);

        var response = await UploadAsync(fixture.Client, token, bytes, "image/jpeg", "reef.jpg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await ReadAsync<DraftDto>(response);
        Assert.Equal(PostState.Draft, draft.State);
        Assert.Equal(PostTopic.Cichlids, draft.Topic);
        Assert.Null(draft.Title);
        Assert.Null(draft.Description);
        Assert.NotNull(draft.Image.Thumb);
        Assert.NotNull(draft.Image.Small);
        Assert.NotNull(draft.Image.Medium);
        Assert.Null(draft.Image.Large);
        Assert.EndsWith(".jpg", draft.Image.Original);

        var profileId = await ResolveProfileIdAsync(subject);

        await using var db = fixture.CreateDbContext();
        var post = await db.Posts.SingleAsync(p => p.Id == draft.Id);
        Assert.Equal(profileId, post.AuthorProfileId);
        Assert.Equal(PostKind.Single, post.Kind);
        Assert.Equal(PostState.Draft, post.State);
        Assert.Equal(PostTopic.Cichlids, post.Topic);
        Assert.Null(post.PublishedAt);

        var postMedia = await db.PostMedia.SingleAsync(pm => pm.PostId == post.Id);
        Assert.Equal(0, postMedia.Sort);

        var mediaItem = await db.MediaItems.SingleAsync(m => m.Id == postMedia.MediaItemId);
        Assert.Equal(profileId, mediaItem.OwnerProfileId);
        Assert.Equal(MediaKind.Photo, mediaItem.Kind);
        Assert.StartsWith($"originals/uploads/{profileId}/", mediaItem.StorageKey);
        Assert.EndsWith(".jpg", mediaItem.StorageKey);
        Assert.Equal("reef.jpg", mediaItem.OriginalFilename);
        Assert.Equal("image/jpeg", mediaItem.ContentType);
        Assert.Equal(bytes.LongLength, mediaItem.ByteSize);
        Assert.Equal(1000, mediaItem.Width);
        Assert.Equal(700, mediaItem.Height);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), mediaItem.ChecksumSha256);

        Assert.Equal(bytes.LongLength, await fixture.ObjectStore.GetSizeAsync(mediaItem.StorageKey));

        var variants = await db.MediaVariants.Where(v => v.MediaItemId == mediaItem.Id).OrderBy(v => v.Width).ToListAsync();
        Assert.Equal(["thumb", "small", "medium"], variants.Select(v => v.Label));
        Assert.Equal([200, 400, 800], variants.Select(v => v.Width!.Value));

        foreach (var variant in variants)
        {
            Assert.Equal($"variants/{variant.Label}/{mediaItem.StorageKey}.jpg", variant.StorageKey);
            Assert.Equal(variant.ByteSize, await fixture.ObjectStore.GetSizeAsync(variant.StorageKey));
        }
    }

    [Fact]
    public async Task Upload_PngIsStoredWithPngExtensionAndContentType()
    {
        var (token, _) = NewCaller();

        var draft = await UploadDraftAsync(fixture.Client, token, Png(600, 400), "image/png");

        await using var db = fixture.CreateDbContext();
        var mediaItem = await LoadMediaItemAsync(db, draft.Id);
        Assert.EndsWith(".png", mediaItem.StorageKey);
        Assert.Equal("image/png", mediaItem.ContentType);
        Assert.Equal(600, mediaItem.Width);
        Assert.Equal(400, mediaItem.Height);
        Assert.True(await fixture.ObjectStore.ExistsAsync(mediaItem.StorageKey));

        var labels = await db.MediaVariants.Where(v => v.MediaItemId == mediaItem.Id).Select(v => v.Label).ToListAsync();
        Assert.Equal(["small", "thumb"], labels.Order());
    }

    [Fact]
    public async Task Upload_WebpIsStoredWithWebpExtensionAndContentType()
    {
        var draft = await UploadDraftAsync(fixture.Client, NewToken(), Webp(500, 300), "image/webp");

        await using var db = fixture.CreateDbContext();
        var mediaItem = await LoadMediaItemAsync(db, draft.Id);
        Assert.EndsWith(".webp", mediaItem.StorageKey);
        Assert.Equal("image/webp", mediaItem.ContentType);
        Assert.Equal(500, mediaItem.Width);
        Assert.True(await fixture.ObjectStore.ExistsAsync(mediaItem.StorageKey));
    }

    [Fact]
    public async Task Upload_ExifRotatedJpegRecordsOrientationCorrectedDimensions()
    {
        var (token, _) = NewCaller();

        var draft = await UploadDraftAsync(fixture.Client, token, OrientedJpeg(1000, 700));

        await using var db = fixture.CreateDbContext();
        var mediaItem = await LoadMediaItemAsync(db, draft.Id);
        Assert.Equal(700, mediaItem.Width);
        Assert.Equal(1000, mediaItem.Height);

        var variantWidths = await db.MediaVariants.Where(v => v.MediaItemId == mediaItem.Id).Select(v => v.Width!.Value).ToListAsync();
        Assert.Equal([200, 400], variantWidths.Order());
        Assert.NotNull(draft.Image.Small);
        Assert.Null(draft.Image.Medium);
    }

    [Fact]
    public async Task Drafts_ListsOnlyTheCallersDraftsNewestFirst()
    {
        var token = NewToken();
        var first = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var second = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var foreign = await UploadDraftAsync(fixture.Client, NewToken(), Jpeg(300, 200));

        var response = await ListDraftsAsync(fixture.Client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var drafts = await ReadAsync<List<DraftDto>>(response);
        Assert.Equal([second.Id, first.Id], drafts.Select(d => d.Id));
        Assert.DoesNotContain(drafts, d => d.Id == foreign.Id);
        Assert.All(drafts, d => Assert.NotNull(d.Image.Thumb));
    }

    [Fact]
    public async Task Drafts_ListsTheTitleAndDescriptionOfADraft()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var untitled = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Posts.Where(p => p.Id == draft.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Title, "Tropheus moorii").SetProperty(p => p.Description, "Kaiser morph"));
        }

        var drafts = await ReadAsync<List<DraftDto>>(await ListDraftsAsync(fixture.Client, token));

        var listed = Assert.Single(drafts, d => d.Id == draft.Id);
        Assert.Equal("Tropheus moorii", listed.Title);
        Assert.Equal("Kaiser morph", listed.Description);
        var listedUntitled = Assert.Single(drafts, d => d.Id == untitled.Id);
        Assert.Null(listedUntitled.Title);
        Assert.Null(listedUntitled.Description);
    }

    [Fact]
    public async Task Drafts_AnonymousReturnsUnauthorized()
    {
        var response = await ListDraftsAsync(fixture.Client, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Draft_IsNotListedInTheGallery()
    {
        var draft = await UploadDraftAsync(fixture.Client, NewToken(), Jpeg(300, 200));

        var gallery = await GetGalleryAsync();

        Assert.DoesNotContain(gallery.Items, p => p.Id == draft.Id);
    }

    [Fact]
    public async Task Publish_ListsThePostWithItsSlugAndRecordsTheOutboxEvent()
    {
        var (token, subject) = NewCaller();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(1000, 700));

        var response = await PublishAsync(
            fixture.Client, token, draft.Id, new PublishPostRequest("  Tropheus moorii  ", "Kaiser morph", "tanks"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = await ReadAsync<PictureDetailDto>(response);
        Assert.Equal(draft.Id, published.Id);
        Assert.Equal("Tropheus moorii", published.Title);
        Assert.Equal("Kaiser morph", published.Description);
        Assert.Equal(PostTopic.Tanks, published.Topic);
        Assert.NotNull(published.PublishedAt);
        Assert.Matches("^[0-9a-z]{10}$", published.Slug);
        Assert.Equal(published.Slug, published.CanonicalSlug);
        Assert.NotNull(published.Image?.Medium);

        var profileId = await ResolveProfileIdAsync(subject);
        await using (var db = fixture.CreateDbContext())
        {
            var post = await db.Posts.SingleAsync(p => p.Id == draft.Id);
            Assert.Equal(PostState.Published, post.State);
            Assert.NotNull(post.PublishedAt);

            var alias = await db.SlugAliases.SingleAsync(s => s.PostId == draft.Id);
            Assert.Equal(published.Slug, alias.Value);
            Assert.True(alias.IsCanonical);

            var outboxEvent = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "post.published" && e.AggregateType == "post" && e.AggregateId == draft.Id.ToString());
            using var payload = JsonDocument.Parse(outboxEvent.Payload);
            Assert.Equal(draft.Id, payload.RootElement.GetProperty("postId").GetInt64());
            Assert.Equal(published.Slug, payload.RootElement.GetProperty("slug").GetString());
            Assert.Equal(profileId, payload.RootElement.GetProperty("authorProfileId").GetInt64());
        }

        var gallery = await GetGalleryAsync();
        Assert.Contains(gallery.Items, p => p.Id == draft.Id && p.Slug == published.Slug);

        var detailResponse = await fixture.Client.GetAsync($"/api/pictures/{published.Slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var drafts = await ReadAsync<List<DraftDto>>(await ListDraftsAsync(fixture.Client, token));
        Assert.DoesNotContain(drafts, d => d.Id == draft.Id);
    }

    [Fact]
    public async Task Publish_WithoutTopicFilesThePostUnderCichlids()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));

        var response = await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Aulonocara", null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = await ReadAsync<PictureDetailDto>(response);
        Assert.Equal(PostTopic.Cichlids, published.Topic);
        Assert.Null(published.Description);
    }

    public static TheoryData<string?, string?, string?> InvalidPublishRequests => new()
    {
        { null, null, null },
        { "   ", null, null },
        { new string('t', 201), null, null },
        { "Title", new string('d', 5001), null },
        { "Title", null, "contest" },
        { "Title", null, "unknown" },
        { "Title", null, "fish" },
    };

    [Theory]
    [MemberData(nameof(InvalidPublishRequests))]
    public async Task Publish_InvalidRequestReturnsBadRequestAndKeepsTheDraft(string? title, string? description, string? topic)
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));

        var response = await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest(title, description, topic));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(PostState.Draft, (await db.Posts.SingleAsync(p => p.Id == draft.Id)).State);
    }

    [Fact]
    public async Task Publish_ByAnotherProfileReturnsNotFound()
    {
        var draft = await UploadDraftAsync(fixture.Client, NewToken(), Jpeg(300, 200));

        var response = await PublishAsync(fixture.Client, NewToken(), draft.Id, new PublishPostRequest("Mine now", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publish_UnknownPostReturnsNotFound()
    {
        var response = await PublishAsync(fixture.Client, NewToken(), long.MaxValue, new PublishPostRequest("Nothing", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publish_TwiceReturnsConflict()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var first = await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Once", null, null));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Twice", null, null));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.SlugAliases.Where(s => s.PostId == draft.Id).ToListAsync());
        Assert.Equal("Once", (await db.Posts.SingleAsync(p => p.Id == draft.Id)).Title);
    }

    [Fact]
    public async Task Publish_AnonymousReturnsUnauthorized()
    {
        var draft = await UploadDraftAsync(fixture.Client, NewToken(), Jpeg(300, 200));

        var response = await fixture.Client.PostAsync(
            $"/api/posts/{draft.Id}/publish", System.Net.Http.Json.JsonContent.Create(new PublishPostRequest("Title", null, null)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Discard_RemovesTheDraftRowsAndStoredObjects()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(1000, 700));

        List<string> storedKeys;
        long mediaItemId;
        await using (var db = fixture.CreateDbContext())
        {
            var mediaItem = await LoadMediaItemAsync(db, draft.Id);
            mediaItemId = mediaItem.Id;
            storedKeys = await db.MediaVariants.Where(v => v.MediaItemId == mediaItem.Id).Select(v => v.StorageKey).ToListAsync();
            storedKeys.Add(mediaItem.StorageKey);
        }

        Assert.Equal(4, storedKeys.Count);

        var response = await DiscardAsync(fixture.Client, token, draft.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.Posts.AnyAsync(p => p.Id == draft.Id));
            Assert.False(await db.PostMedia.AnyAsync(pm => pm.PostId == draft.Id));
            Assert.False(await db.MediaItems.AnyAsync(m => m.Id == mediaItemId));
            Assert.False(await db.MediaVariants.AnyAsync(v => v.MediaItemId == mediaItemId));
        }

        foreach (var key in storedKeys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key), $"Object '{key}' still exists.");
        }
    }

    [Fact]
    public async Task Discard_ByAnotherProfileReturnsNotFoundAndKeepsTheDraft()
    {
        var draft = await UploadDraftAsync(fixture.Client, NewToken(), Jpeg(300, 200));

        var response = await DiscardAsync(fixture.Client, NewToken(), draft.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Posts.AnyAsync(p => p.Id == draft.Id));
    }

    [Fact]
    public async Task Discard_UnknownPostReturnsNotFound()
    {
        var response = await DiscardAsync(fixture.Client, NewToken(), long.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Discard_PublishedPostReturnsConflict()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var publish = await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Keeper", null, null));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var response = await DiscardAsync(fixture.Client, token, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Posts.AnyAsync(p => p.Id == draft.Id));
    }

    [Fact]
    public async Task Discard_ConcurrentDiscardsOfOneDraftSucceedOnceAndFindNothingOnce()
    {
        for (var round = 0; round < 5; round++)
        {
            var token = NewToken();
            var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));

            var responses = await Task.WhenAll(
                DiscardAsync(fixture.Client, token, draft.Id),
                DiscardAsync(fixture.Client, token, draft.Id));

            Assert.Equal(
                [HttpStatusCode.NoContent, HttpStatusCode.NotFound],
                responses.Select(r => r.StatusCode).OrderBy(c => (int)c));
        }
    }

    [Fact]
    public async Task PublishAndDiscard_RacingOnOneDraftExactlyOneWins()
    {
        for (var round = 0; round < 5; round++)
        {
            var token = NewToken();
            var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));

            var responses = await Task.WhenAll(
                PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Race", null, null)),
                DiscardAsync(fixture.Client, token, draft.Id));
            var (publish, discard) = (responses[0].StatusCode, responses[1].StatusCode);

            // Either the publish wins and the discard sees a published post, or the discard wins
            // and the publish finds no post.
            Assert.True(
                (publish, discard) is (HttpStatusCode.OK, HttpStatusCode.Conflict) or (HttpStatusCode.NotFound, HttpStatusCode.NoContent),
                $"Unexpected outcome: publish {publish}, discard {discard}.");
        }
    }

    private async Task<PagedResponse<PictureListItemDto>> GetGalleryAsync()
    {
        var response = await fixture.Client.GetAsync("/api/pictures?limit=50");
        response.EnsureSuccessStatusCode();
        return await ReadAsync<PagedResponse<PictureListItemDto>>(response);
    }

    private async Task<long> ResolveProfileIdAsync(string subject)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ProfileIdentities
            .Where(i => i.Provider == "oidc" && i.Subject == subject)
            .Select(i => i.ProfileId)
            .SingleAsync();
    }

    private static async Task<Cichlids.Domain.Entities.MediaItem> LoadMediaItemAsync(
        Cichlids.Infrastructure.Persistence.CichlidsDbContext db, long postId)
    {
        var mediaItemId = await db.PostMedia.Where(pm => pm.PostId == postId).Select(pm => pm.MediaItemId).SingleAsync();
        return await db.MediaItems.SingleAsync(m => m.Id == mediaItemId);
    }

    private async Task<List<string>> ListStoredKeysAsync(string prefix)
    {
        var keys = new List<string>();
        await foreach (var key in fixture.ObjectStore.ListKeysAsync(prefix))
        {
            keys.Add(key);
        }

        return keys;
    }
}
