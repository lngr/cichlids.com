using System.Net;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Uploads;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Uploads;

[Collection(UploadsCollection.Name)]
public class MediaOverlayTests(UploadsFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Given_golden_media_when_uploading_and_removing_drafts_then_only_environment_objects_change(bool cleanup)
    {
        var writePrefix = $"environments/{Guid.NewGuid():N}/";
        using var host = fixture.CreateOverlayHost(writePrefix);
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<IObjectStore>();
        var key = $"variants/thumb/media-{Guid.NewGuid():N}.jpg";
        var goldenKey = "golden/v1/media/" + key;
        await fixture.ObjectStore.PutAsync(goldenKey, new MemoryStream([1, 2, 3]), "image/jpeg");
        Assert.True(await store.ExistsAsync(key));
        Assert.Equal(3, (await store.HeadAsync(key))!.Size);
        Assert.Equal(3, await store.GetSizeAsync(key));
        await using (var body = await store.GetAsync(key))
        {
            Assert.Equal(1, body!.ReadByte());
        }
        var urls = new MediaUrlBuilder(store);
        Assert.Equal(fixture.PublicBaseUrl + "/" + goldenKey, urls.BuildThumbnail(key, null));
        Assert.Equal(fixture.PublicBaseUrl + "/" + goldenKey, urls.BuildBest(new Dictionary<string, string> { ["thumb"] = key }));
        var goldenBefore = await GoldenKeysAsync();

        var token = NewToken();
        var draft = await UploadDraftAsync(client, token, Jpeg(300, 200));
        await using var db = fixture.CreateDbContext();
        var mediaId = await db.PostMedia.Where(m => m.PostId == draft.Id).Select(m => m.MediaItemId).SingleAsync();
        var original = await db.MediaItems.Where(m => m.Id == mediaId).Select(m => m.StorageKey).SingleAsync();
        var keys = await db.MediaVariants.Where(v => v.MediaItemId == mediaId).Select(v => v.StorageKey).ToListAsync();
        keys.Add(original);
        foreach (var uploadedKey in keys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(writePrefix + uploadedKey));
            Assert.False(await fixture.ObjectStore.ExistsAsync(uploadedKey));
            Assert.False(await fixture.ObjectStore.ExistsAsync("golden/v1/media/" + uploadedKey));
        }
        Assert.StartsWith(fixture.PublicBaseUrl + "/" + writePrefix, draft.Image!.Thumb);

        // A golden variant with the same logical key must survive draft removal.
        var shared = "golden/v1/media/" + keys[0];
        await fixture.ObjectStore.PutAsync(shared, new MemoryStream([7, 8]), "image/jpeg");
        if (cleanup)
        {
            await db.Posts.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
            using var scope = host.Services.CreateScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None) >= 1);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, (await DiscardAsync(client, token, draft.Id)).StatusCode);
        }
        foreach (var uploadedKey in keys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(writePrefix + uploadedKey));
        }
        Assert.Equal(2, await fixture.ObjectStore.GetSizeAsync(shared));
        Assert.Equal(goldenBefore.Append(shared).Order(), (await GoldenKeysAsync()).Order());
        Assert.False(await db.Posts.AnyAsync(p => p.Id == draft.Id));
    }

    private async Task<List<string>> GoldenKeysAsync()
    {
        var keys = new List<string>();
        await foreach (var key in fixture.ObjectStore.ListKeysAsync("golden/")) keys.Add(key);
        return keys;
    }
}
