// Story: TASK-3.37
using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Uploads;

/// <summary>
/// Limits on what a member may upload: the number of open drafts, the number of uploads per
/// hour, and the cleanup of drafts that were never touched.
/// </summary>
[Collection(UploadsCollection.Name)]
public class UploadLimitsTests(UploadsFixture fixture)
{
    [Fact]
    public async Task Upload_MemberWithFiftyOpenDraftsGetsDraftLimitReached()
    {
        var (token, subject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, token)).EnsureSuccessStatusCode();
        var profileId = await ProfileIdAsync(subject);
        await InsertDraftsAsync(profileId, 50, DateTimeOffset.UtcNow);

        var response = await UploadAsync(fixture.Client, token, Jpeg(300, 200), "image/jpeg");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "draft_limit_reached");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(50, await db.Posts.CountAsync(p => p.AuthorProfileId == profileId));
        Assert.False(await db.MediaItems.AnyAsync(m => m.OwnerProfileId == profileId));
    }

    [Fact]
    public async Task Upload_MemberWithFortyNineOpenDraftsStillMayUpload()
    {
        var (token, subject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, token)).EnsureSuccessStatusCode();
        var profileId = await ProfileIdAsync(subject);
        await InsertDraftsAsync(profileId, 49, DateTimeOffset.UtcNow);

        var response = await UploadAsync(fixture.Client, token, Jpeg(300, 200), "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_PublishedAndDeletedPostsDoNotCountAsOpenDrafts()
    {
        var (token, subject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, token)).EnsureSuccessStatusCode();
        var profileId = await ProfileIdAsync(subject);
        await InsertDraftsAsync(profileId, 49, DateTimeOffset.UtcNow);
        await using (var db = fixture.CreateDbContext())
        {
            db.Posts.Add(new Post
            {
                AuthorProfileId = profileId, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
                State = PostState.Published, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow,
            });
            db.Posts.Add(new Post
            {
                AuthorProfileId = profileId, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
                State = PostState.Draft, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, DeletedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await UploadAsync(fixture.Client, token, Jpeg(300, 200), "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_DraftLimitOfOneMemberDoesNotAffectAnother()
    {
        var (fullToken, fullSubject) = NewCaller();
        (await ListDraftsAsync(fixture.Client, fullToken)).EnsureSuccessStatusCode();
        await InsertDraftsAsync(await ProfileIdAsync(fullSubject), 50, DateTimeOffset.UtcNow);

        var response = await UploadAsync(fixture.Client, NewToken(), Jpeg(300, 200), "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_AboveTheHourlyLimitGetsUploadRateLimited()
    {
        var token = NewToken();
        var client = fixture.ClientWithTwoUploadsPerHour;

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, token, Jpeg(300, 200), "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, token, Jpeg(300, 200), "image/jpeg")).StatusCode);
        var refused = await UploadAsync(client, token, Jpeg(300, 200), "image/jpeg");

        await AssertProblemAsync(refused, HttpStatusCode.TooManyRequests, "upload_rate_limited");
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, NewToken(), Jpeg(300, 200), "image/jpeg")).StatusCode);
    }

    [Fact]
    public async Task Upload_CountsStoredUploadsOfTheLastHourSoTheLimitSurvivesARestart()
    {
        var (token, subject) = NewCaller();
        var client = fixture.ClientWithTwoUploadsPerHour;
        await UploadDraftAsync(client, token, Jpeg(300, 200));
        await UploadDraftAsync(client, token, Jpeg(300, 200));
        var profileId = await ProfileIdAsync(subject);

        // Uploads older than an hour no longer count.
        await using (var db = fixture.CreateDbContext())
        {
            await db.MediaItems
                .Where(m => m.OwnerProfileId == profileId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-61)));
        }

        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, token, Jpeg(300, 200), "image/jpeg")).StatusCode);
    }

    [Fact]
    public async Task Cleanup_RemovesDraftsUntouchedForThirtyDaysWithTheirObjects()
    {
        var (token, subject) = NewCaller();
        var stale = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var fresh = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var published = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        (await PublishAsync(fixture.Client, token, published.Id, new PublishPostRequest("Old but published", null, "cichlids")))
            .EnsureSuccessStatusCode();
        var profileId = await ProfileIdAsync(subject);

        var staleKeys = await StoredKeysOfAsync(stale.Id);
        var freshKeys = await StoredKeysOfAsync(fresh.Id);
        var publishedKeys = await StoredKeysOfAsync(published.Id);
        Assert.NotEmpty(staleKeys);
        await SetCreatedAtAsync(stale.Id, DateTimeOffset.UtcNow.AddDays(-31));
        await SetCreatedAtAsync(fresh.Id, DateTimeOffset.UtcNow.AddDays(-29));
        await SetCreatedAtAsync(published.Id, DateTimeOffset.UtcNow.AddDays(-90));

        using var scope = fixture.Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<DraftCleanupService>();
        var removed = await cleanup.RunOnceAsync(CancellationToken.None);

        Assert.True(removed >= 1);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Posts.AnyAsync(p => p.Id == stale.Id));
        Assert.False(await db.PostMedia.AnyAsync(pm => pm.PostId == stale.Id));
        Assert.False(await db.MediaItems.AnyAsync(m => m.OwnerProfileId == profileId && staleKeys.Contains(m.StorageKey)));
        foreach (var key in staleKeys)
        {
            Assert.False(await fixture.ObjectStore.ExistsAsync(key), $"Object '{key}' of the stale draft is still stored.");
        }

        Assert.True(await db.Posts.AnyAsync(p => p.Id == fresh.Id));
        Assert.True(await db.Posts.AnyAsync(p => p.Id == published.Id));
        foreach (var key in freshKeys.Concat(publishedKeys))
        {
            Assert.True(await fixture.ObjectStore.ExistsAsync(key), $"Object '{key}' was deleted.");
        }
    }

    [Fact]
    public async Task Cleanup_KeepsDraftCreatedLongAgoButChangedRecently()
    {
        var token = NewToken();
        var edited = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        var untouched = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        await SetTimestampsAsync(edited.Id, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-2));
        await SetTimestampsAsync(untouched.Id, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-31));

        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Posts.AnyAsync(p => p.Id == edited.Id));
        Assert.False(await db.Posts.AnyAsync(p => p.Id == untouched.Id));
    }

    [Fact]
    public async Task UploadSetsUpdatedAtToTheCreationMoment()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));

        await using var db = fixture.CreateDbContext();
        var post = await db.Posts.SingleAsync(p => p.Id == draft.Id);
        Assert.Equal(post.CreatedAt, post.UpdatedAt);
    }

    [Fact]
    public async Task PublishMovesUpdatedAtForward()
    {
        var token = NewToken();
        var draft = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        await SetTimestampsAsync(draft.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-10));

        (await PublishAsync(fixture.Client, token, draft.Id, new PublishPostRequest("Title", null, "cichlids")))
            .EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        var post = await db.Posts.SingleAsync(p => p.Id == draft.Id);
        Assert.True(post.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Cleanup_RunTwiceIsIdempotentAndRunsConcurrentlyWithoutError()
    {
        var token = NewToken();
        var stale = await UploadDraftAsync(fixture.Client, token, Jpeg(300, 200));
        await SetCreatedAtAsync(stale.Id, DateTimeOffset.UtcNow.AddDays(-40));

        var passes = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            using var scope = fixture.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None);
        }));
        using var again = fixture.Services.CreateScope();
        await again.ServiceProvider.GetRequiredService<DraftCleanupService>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, passes.Sum());
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Posts.AnyAsync(p => p.Id == stale.Id));
    }

    private async Task<long> ProfileIdAsync(string subject)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ProfileIdentities
            .Where(i => i.Provider == "oidc" && i.Subject == subject)
            .Select(i => i.ProfileId)
            .SingleAsync();
    }

    private async Task InsertDraftsAsync(long profileId, int count, DateTimeOffset createdAt)
    {
        await using var db = fixture.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            db.Posts.Add(new Post
            {
                AuthorProfileId = profileId, Kind = PostKind.Single, Topic = PostTopic.Cichlids,
                State = PostState.Draft, CreatedAt = createdAt, UpdatedAt = createdAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SetCreatedAtAsync(long postId, DateTimeOffset createdAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.CreatedAt, createdAt).SetProperty(p => p.UpdatedAt, createdAt));
    }

    private async Task SetTimestampsAsync(long postId, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.CreatedAt, createdAt).SetProperty(p => p.UpdatedAt, updatedAt));
    }

    private async Task<List<string>> StoredKeysOfAsync(long postId)
    {
        await using var db = fixture.CreateDbContext();
        var mediaItemIds = await db.PostMedia.Where(pm => pm.PostId == postId).Select(pm => pm.MediaItemId).ToListAsync();
        return await db.MediaItems.Where(m => mediaItemIds.Contains(m.Id)).Select(m => m.StorageKey)
            .Concat(db.MediaVariants.Where(v => mediaItemIds.Contains(v.MediaItemId)).Select(v => v.StorageKey))
            .ToListAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("detail").GetString()));
    }
}
