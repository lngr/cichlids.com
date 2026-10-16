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
    public async Task Given_golden_media_when_uploading_and_removing_drafts_then_environment_objects_stay_and_golden_objects_are_untouched(bool cleanup)
    {
        var writePrefix = $"environments/{Guid.NewGuid():N}/";
        using var host = fixture.CreateOverlayHost(writePrefix);
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<IObjectStore>();
        var key = $"variants/thumb/media-{Random.Shared.NextInt64(1, long.MaxValue)}.jpg";
        var goldenKey = "golden/v1/media/" + key;
        await fixture.SeedGoldenAsync(goldenKey, [1, 2, 3]);
        Assert.True(await store.ExistsAsync(goldenKey));
        Assert.Equal(3, (await store.HeadAsync(goldenKey))!.Size);
        Assert.Equal(3, await store.GetSizeAsync(goldenKey));
        await using (var body = await store.GetAsync(goldenKey))
        {
            Assert.Equal(1, body!.ReadByte());
        }
        var urls = new MediaUrlBuilder(store);
        Assert.Equal(fixture.PublicBaseUrl + "/" + goldenKey, urls.BuildThumbnail(goldenKey, null));
        Assert.Equal(fixture.PublicBaseUrl + "/" + goldenKey, urls.BuildBest(new Dictionary<string, string> { ["thumb"] = goldenKey }));

        var token = NewToken();
        var draft = await UploadDraftAsync(client, token, Jpeg(300, 200));
        await using var db = fixture.CreateDbContext();
        var mediaId = await db.MediaLinks.Where(l => l.StoryId == draft.Id).Select(l => l.MediaItemId).SingleAsync();
        var original = await db.MediaItems.Where(m => m.Id == mediaId).Select(m => m.StorageKey).SingleAsync();
        var keys = await db.MediaVariants.Where(v => v.MediaItemId == mediaId).Select(v => v.StorageKey).ToListAsync();
        keys.Add(original);
        foreach (var uploadedKey in keys)
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(writePrefix + uploadedKey));
            Assert.False(await fixture.ObjectStore.ExistsAsync(uploadedKey));
            Assert.False(await fixture.ObjectStore.ExistsAsync("golden/v1/media/" + uploadedKey));
        }
        Assert.StartsWith("/api/media/photos/", draft.Image!.Thumb);
        using var served = await client.GetAsync(draft.Image.Thumb);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        // A golden variant with the same logical key must survive draft removal.
        var shared = "golden/v1/media/" + keys[0];
        await fixture.SeedGoldenAsync(shared, [7, 8]);
        if (cleanup)
        {
            await db.Stories.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(
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
            Assert.True(await fixture.ObjectStore.ExistsAsync(writePrefix + uploadedKey));
        }
        Assert.Equal(2, await fixture.ObjectStore.GetSizeAsync(shared));
        Assert.Equal(3, await store.GetSizeAsync(goldenKey));
        Assert.False(await db.Stories.AnyAsync(p => p.Id == draft.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Given_golden_references_in_a_draft_when_discarded_or_cleaned_then_shared_objects_survive(bool cleanup)
    {
        using var host = fixture.CreateOverlayHost($"environments/{Guid.NewGuid():N}/");
        using var client = host.CreateClient();
        var token = NewToken();
        var draft = await UploadDraftAsync(client, token, Jpeg(300, 200));
        await using var db = fixture.CreateDbContext();
        var mediaId = await db.MediaLinks.Where(l => l.StoryId == draft.Id).Select(l => l.MediaItemId).SingleAsync();
        var goldenKey = $"golden/v1/media/variants/thumb/media-{mediaId}.jpg";
        await fixture.SeedGoldenAsync(goldenKey, [1, 2, 3]);
        await db.MediaItems.Where(m => m.Id == mediaId).ExecuteUpdateAsync(s => s.SetProperty(m => m.StorageKey, goldenKey));
        await db.MediaVariants.Where(v => v.MediaItemId == mediaId).ExecuteUpdateAsync(s => s.SetProperty(v => v.StorageKey, goldenKey));
        if (cleanup)
        {
            await db.Stories.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
            using var scope = host.Services.CreateScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None) >= 1);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, (await DiscardAsync(client, token, draft.Id)).StatusCode);
        }
        Assert.False(await db.Stories.AnyAsync(p => p.Id == draft.Id));
        Assert.Equal(3, await fixture.ObjectStore.GetSizeAsync(goldenKey));
    }
}
