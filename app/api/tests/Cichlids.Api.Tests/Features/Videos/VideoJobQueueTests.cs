using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Cichlids.Api.Tests.Features.Videos;

[Collection(ApiCollection.Name)]
public sealed class VideoJobQueueTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly VideoProcessingOptions _options = new() { MaxAttempts = 3, RetryDelay = TimeSpan.FromMinutes(1), Lease = TimeSpan.FromMinutes(30) };

    public async Task InitializeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.VideoJobs.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private VideoJobQueue Queue(CichlidsDbContext db) => new(db, Options.Create(_options), _clock);

    private async Task<long> EnqueueAsync(DateTimeOffset? runAfter = null)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem { Kind = MediaKind.Video, StorageKey = $"private/video/0/{Guid.NewGuid():N}.mp4", CreatedAt = _clock.GetUtcNow() };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.VideoJobs.Add(new VideoJob
        {
            MediaItemId = item.Id, Status = VideoJobStatus.Queued, RunAfter = runAfter ?? _clock.GetUtcNow(), CreatedAt = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<VideoJob> JobOfAsync(long mediaItemId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.VideoJobs.AsNoTracking().SingleAsync(j => j.MediaItemId == mediaItemId);
    }

    [Fact]
    public async Task GivenAQueuedJob_WhenClaimed_ThenItRunsWithOneAttemptAndALease()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();

        var job = await Queue(db).ClaimAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(item, job.MediaItemId);
        var stored = await JobOfAsync(item);
        Assert.Equal((VideoJobStatus.Running, 1), (stored.Status, stored.Attempts));
        Assert.Equal(_clock.GetUtcNow() + _options.Lease, stored.LockedUntil);
        Assert.Null(await Queue(db).ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GivenManyWorkersAndJobs_WhenClaimingAtTheSameTime_ThenEveryJobIsClaimedExactlyOnce()
    {
        var items = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            items.Add(await EnqueueAsync());
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            return await Queue(db).ClaimAsync(CancellationToken.None);
        }));

        var claimed = claims.Where(c => c is not null).Select(c => c!.MediaItemId).ToList();
        Assert.Equal(items.Order(), claimed.Order());
    }

    [Fact]
    public async Task GivenOneJobAndTwoWorkers_ThenOnlyOneWorkerGetsIt()
    {
        await EnqueueAsync();

        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            return await Queue(db).ClaimAsync(CancellationToken.None);
        }));

        Assert.Single(claims, c => c is not null);
    }

    [Fact]
    public async Task GivenAJobDueLater_ThenItIsNotClaimedBeforeItsTime()
    {
        var item = await EnqueueAsync(_clock.GetUtcNow().AddMinutes(5));
        await using var db = fixture.CreateDbContext();

        Assert.Null(await Queue(db).ClaimAsync(CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(item, (await Queue(db).ClaimAsync(CancellationToken.None))?.MediaItemId);
    }

    [Fact]
    public async Task GivenARunningJobWhoseLeaseHasEnded_ThenAnotherWorkerClaimsItAgain()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();
        await Queue(db).ClaimAsync(CancellationToken.None);

        _clock.Advance(_options.Lease - TimeSpan.FromSeconds(1));
        Assert.Null(await Queue(db).ClaimAsync(CancellationToken.None));
        _clock.Advance(TimeSpan.FromSeconds(2));
        var again = await Queue(db).ClaimAsync(CancellationToken.None);

        Assert.Equal(item, again?.MediaItemId);
        Assert.Equal(2, (await JobOfAsync(item)).Attempts);
    }

    [Fact]
    public async Task GivenAJobWhoseLeaseEndedAfterTheLastAttempt_ThenClaimingMarksItFailed()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            Assert.NotNull(await Queue(db).ClaimAsync(CancellationToken.None));
            _clock.Advance(_options.Lease + TimeSpan.FromSeconds(1));
        }

        Assert.Null(await Queue(db).ClaimAsync(CancellationToken.None));

        var stored = await JobOfAsync(item);
        Assert.Equal(VideoJobStatus.Failed, stored.Status);
        Assert.Equal(VideoProcessingErrors.ProcessingFailed, stored.ErrorCode);
        Assert.Single(await FailedEventsAsync(item));
    }

    [Fact]
    public async Task GivenATransientFailure_ThenTheJobIsQueuedAgainAfterAGrowingDelay()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();
        var queue = Queue(db);

        var first = await queue.ClaimAsync(CancellationToken.None);
        Assert.Equal(VideoJobStatus.Queued, await queue.FailAsync(first!, VideoProcessingErrors.ProcessingFailed, permanent: false, CancellationToken.None));
        var afterFirst = await JobOfAsync(item);
        Assert.Equal(_clock.GetUtcNow() + _options.RetryDelay, afterFirst.RunAfter);
        Assert.Null(afterFirst.LockedUntil);
        Assert.Null(await queue.ClaimAsync(CancellationToken.None));

        _clock.Advance(_options.RetryDelay);
        var second = await queue.ClaimAsync(CancellationToken.None);
        await queue.FailAsync(second!, VideoProcessingErrors.ProcessingFailed, permanent: false, CancellationToken.None);
        Assert.Equal(_clock.GetUtcNow() + (_options.RetryDelay * 2), (await JobOfAsync(item)).RunAfter);
        Assert.Empty(await FailedEventsAsync(item));
    }

    [Fact]
    public async Task GivenTheLastAttemptFails_ThenTheJobIsFailedWithAnEventInTheSameTransaction()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();
        var queue = Queue(db);

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            var job = await queue.ClaimAsync(CancellationToken.None);
            var status = await queue.FailAsync(job!, VideoProcessingErrors.ProcessingFailed, permanent: false, CancellationToken.None);
            Assert.Equal(attempt < _options.MaxAttempts ? VideoJobStatus.Queued : VideoJobStatus.Failed, status);
        }

        var stored = await JobOfAsync(item);
        Assert.Equal((VideoJobStatus.Failed, _options.MaxAttempts), (stored.Status, stored.Attempts));
        Assert.NotNull(stored.FinishedAt);
        Assert.Single(await FailedEventsAsync(item));
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(await queue.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GivenAPermanentFailure_ThenTheJobFailsAtOnceWithItsCode()
    {
        var item = await EnqueueAsync();
        await using var db = fixture.CreateDbContext();
        var queue = Queue(db);

        var job = await queue.ClaimAsync(CancellationToken.None);
        var status = await queue.FailAsync(job!, "video_unreadable", permanent: true, CancellationToken.None);

        Assert.Equal(VideoJobStatus.Failed, status);
        var stored = await JobOfAsync(item);
        Assert.Equal(("video_unreadable", 1), (stored.ErrorCode, stored.Attempts));
        Assert.Single(await FailedEventsAsync(item));
    }

    private async Task<List<OutboxEvent>> FailedEventsAsync(long mediaItemId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.OutboxEvents.AsNoTracking()
            .Where(e => e.EventType == "video.failed" && e.AggregateId == mediaItemId.ToString()).ToListAsync();
    }
}
