using System.Data.Common;
using Cichlids.Api.Features.Discover;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Api.Tests.Features.Discover;

[Collection("Discover")]
public class DiscoverPerformanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenCancelledComputation_WhenAnotherRegionRetries_ThenTheCacheCanBeFilled()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        using var cancellation = new CancellationTokenSource();
        var counter = new QueryCounter(() =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });
        await using (var db = fixture.CreateDbContext(counter))
        {
            var service = new DiscoverQueryService(db, new TankTimeLapseQueryService(db, store), store, cache, TimeProvider.System);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync([TankRegion.LakeMalawi], cancellation.Token));
        }
        await using var retryDb = fixture.CreateDbContext();
        var retry = new DiscoverQueryService(retryDb, new TankTimeLapseQueryService(retryDb, store), store, cache, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(TankRegion.Mixed, Assert.Single((await retry.GetAsync([TankRegion.Mixed], timeout.Token)).Regions).Region);
    }

    [Fact]
    public async Task GivenDifferentRegionSets_WhenRequestedConcurrently_ThenOneArchiveIsComputed()
    {
        using var cache = new RecordingCache();
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var counter = new QueryCounter();
        var selections = DiscoverQueryService.SelectableRegions.Select(r => new[] { r })
            .Append(DiscoverQueryService.SelectableRegions).ToArray();
        var results = await Task.WhenAll(selections.Select(async selection =>
        {
            await using var db = fixture.CreateDbContext(counter);
            var service = new DiscoverQueryService(db, new TankTimeLapseQueryService(db, store), store, cache, TimeProvider.System);
            return await service.GetAsync(selection, default);
        }));
        Assert.Equal(1, counter.TankRatingQueries);
        var entry = Assert.Single(cache.Entries);
        Assert.Equal(TimeSpan.FromMinutes(15), entry.AbsoluteExpirationRelativeToNow);
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        Assert.Equal($"discover:{System.Globalization.ISOWeek.GetYear(now)}:{System.Globalization.ISOWeek.GetWeekOfYear(now)}", entry.Key);
        for (var i = 0; i < selections.Length; i++)
        {
            Assert.Equal(selections[i].Order(), results[i].Regions.Select(r => r.Region));
            Assert.Equal(results[0].GlobalPictures, results[i].GlobalPictures);
        }
        var count = counter.Total;
        await using var nextDb = fixture.CreateDbContext(counter);
        var next = new DiscoverQueryService(nextDb, new TankTimeLapseQueryService(nextDb, store), store, cache, TimeProvider.System);
        var filtered = await next.GetAsync([TankRegion.Mixed, TankRegion.LakeMalawi, TankRegion.Mixed], default);
        Assert.Equal(new[] { TankRegion.LakeMalawi, TankRegion.Mixed }, filtered.Regions.Select(r => r.Region));
        Assert.Equal(1, counter.TankRatingQueries);
        Assert.Equal(count + 2, counter.Total);
    }

    [Fact]
    public async Task GivenDistinctPhotosAcrossSources_WhenDiscoverRuns_ThenOnlyEightPhotoCandidatesEnterSeries()
    {
        var counter = new QueryCounter();
        await using var db = fixture.CreateDbContext(counter);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var candidateIds = new List<long>();
        var excludedIds = new List<long>();
        foreach (var count in new[] { 7, 8 })
        {
            var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Candidate", Region = TankRegion.Mixed, State = TankState.Published };
            db.Tanks.Add(tank);
            await db.SaveChangesAsync();
            var photos = Enumerable.Range(0, count).Select(i => new MediaItem
            {
                Kind = MediaKind.Photo, StorageKey = Guid.NewGuid().ToString(),
                CreatedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z").AddMonths(i == 7 ? 12 : i),
            }).ToArray();
            db.MediaItems.AddRange(photos);
            await db.SaveChangesAsync();
            tank.MainMediaId = photos[0].Id;
            db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = photos[0].Id });
            db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = photos[0].Id, Section = TankMediaSection.Decoration });
            foreach (var photo in photos)
                db.MediaVariants.Add(new MediaVariant { MediaItemId = photo.Id, Label = "large", StorageKey = $"variants/large/candidate/{photo.Id}.jpg" });
            foreach (var photo in photos.Skip(1))
            {
                var post = new Post { AuthorProfileId = fixture.Seed.Alice.Id, TankId = tank.Id,
                    State = PostState.Published, PublishedAt = photo.CreatedAt };
                db.Posts.Add(post);
                await db.SaveChangesAsync();
                db.PostMedia.Add(new PostMedia { PostId = post.Id, MediaItemId = photo.Id });
            }
            var video = new MediaItem { Kind = MediaKind.Video, StorageKey = Guid.NewGuid().ToString() };
            var draftPhoto = new MediaItem { Kind = MediaKind.Photo, StorageKey = Guid.NewGuid().ToString() };
            var draft = new Post { AuthorProfileId = fixture.Seed.Alice.Id, TankId = tank.Id, State = PostState.Draft };
            db.AddRange(video, draftPhoto, draft);
            await db.SaveChangesAsync();
            db.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = video.Id });
            db.PostMedia.Add(new PostMedia { PostId = draft.Id, MediaItemId = draftPhoto.Id });
            await db.SaveChangesAsync();
            (count == 8 ? candidateIds : excludedIds).Add(tank.Id);
        }
        var service = new DiscoverQueryService(db, new TankTimeLapseQueryService(db, store), store, cache, TimeProvider.System);
        var result = await service.GetAsync([TankRegion.Mixed], default);
        Assert.All(candidateIds, id => Assert.Contains(id, counter.SeriesTankIds));
        Assert.All(excludedIds, id => Assert.DoesNotContain(id, counter.SeriesTankIds));
        Assert.All(candidateIds, id => Assert.Contains(Assert.Single(result.Regions).TimeLapses, t => t.Id == id));
    }

    private sealed class QueryCounter(Action? beforeTankRatings = null) : DbCommandInterceptor
    {
        private int _total;
        private int _tankRatingQueries;
        public int Total => _total;
        public int TankRatingQueries => _tankRatingQueries;
        public System.Collections.Concurrent.ConcurrentBag<long> SeriesTankIds { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _total);
            if (command.CommandText.Contains("GROUP BY r.tank_id", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _tankRatingQueries);
                beforeTankRatings?.Invoke();
            }
            if (command.CommandText.Contains("t.adopted_at", StringComparison.Ordinal)
                && command.CommandText.Contains("ANY (", StringComparison.Ordinal))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is IEnumerable<long> ids)
                        foreach (var id in ids) SeriesTankIds.Add(id);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());
        public System.Collections.Concurrent.ConcurrentBag<ICacheEntry> Entries { get; } = [];
        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);
        public void Remove(object key) => _inner.Remove(key);
        public void Dispose() => _inner.Dispose();
        public ICacheEntry CreateEntry(object key)
        {
            var entry = _inner.CreateEntry(key);
            Entries.Add(entry);
            return entry;
        }
    }
}
