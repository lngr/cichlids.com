// Story: task-3.16
using System.Net;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Api.Features.Uploads;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Uploads;

/// <summary>
/// End-to-end path of the photo upload: an authenticated user uploads a photo and publishes the
/// resulting draft, after which the post with its media item exists, every variant is stored in
/// the object store and the picture leads the gallery listing.
/// </summary>
[Collection(UploadsCollection.Name)]
public class UploadPublishFlowTests(UploadsFixture fixture)
{
    [Fact]
    public async Task UploadedAndPublishedPhotoAppearsInTheGalleryWithStoredVariants()
    {
        var token = NewToken();

        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(1000, 700));

        var publishResponse = await PublishAsync(
            fixture.Client, token, draft.Id, new PublishPostRequest("Frontosa colony", "Six males, one tank", "cichlids"));
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await ReadAsync<PictureDetailDto>(publishResponse);

        var listResponse = await fixture.Client.GetAsync("/api/pictures");
        listResponse.EnsureSuccessStatusCode();
        var gallery = await ReadAsync<PagedResponse<PictureListItemDto>>(listResponse);
        var newest = gallery.Items[0];
        Assert.Equal(draft.Id, newest.Id);
        Assert.Equal(published.Slug, newest.Slug);
        Assert.Equal("Frontosa colony", newest.Title);

        var detailResponse = await fixture.Client.GetAsync($"/api/pictures/{published.Slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadAsync<PictureDetailDto>(detailResponse);
        Assert.Equal(draft.Id, detail.Id);
        Assert.NotNull(detail.Image);
        Assert.NotNull(detail.Image!.Thumb);
        Assert.NotNull(detail.Image.Small);
        Assert.NotNull(detail.Image.Medium);

        await using var db = fixture.CreateDbContext();
        var mediaItemId = await db.PostMedia.Where(pm => pm.PostId == draft.Id).Select(pm => pm.MediaItemId).SingleAsync();
        var mediaItem = await db.MediaItems.SingleAsync(m => m.Id == mediaItemId);
        var variantKeys = await db.MediaVariants.Where(v => v.MediaItemId == mediaItemId).Select(v => v.StorageKey).ToListAsync();

        Assert.Equal(3, variantKeys.Count);
        Assert.True(await fixture.ObjectStore.ExistsAsync(mediaItem.StorageKey));
        foreach (var key in variantKeys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key), $"Variant object '{key}' is missing.");
        }
    }
}
