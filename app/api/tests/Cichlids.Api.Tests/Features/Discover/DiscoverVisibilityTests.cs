using System.Text.Json;
using Cichlids.Api.Features.Discover;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Api.Tests.Features.Discover;

/// <summary>
/// A tank that is not public never appears in Discover: not in the hall of fame, as the tank of the
/// week, among the showcase time-lapses, nor through a picture linked to it, and none of its media
/// URLs leaves the service. The cached rankings are filtered at read time, so a tank switched to
/// private after the cache was filled disappears at once.
/// </summary>
[Collection("Discover")]
public sealed class DiscoverVisibilityTests(ApiFixture fixture)
{
    private sealed record Seeded(long Tank, long Picture, string MediaMarker);

    private async Task<Seeded> SeedTankAsync(CichlidsDbContextAccessor db, TankVisibility level, TankRegion region)
    {
        var tank = new Tank
        {
            ProfileId = fixture.Seed.Alice.Id, Title = $"Discover visibility {Guid.NewGuid():N}", Region = region, State = TankState.Published,
            ProfileVisibility = level, StreamVisibility = level, MediaPlacedPublic = level == TankVisibility.Public,
        };
        db.Context.Tanks.Add(tank);
        await db.Context.SaveChangesAsync();
        var marker = $"vis{tank.Id}x";
        for (var i = 0; i < 8; i++)
        {
            var media = new MediaItem
            {
                StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo,
                CreatedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z").AddMonths(i == 7 ? 12 : i),
            };
            db.Context.MediaItems.Add(media);
            await db.Context.SaveChangesAsync();
            db.Context.TankMedia.Add(new TankMedia { TankId = tank.Id, MediaItemId = media.Id, Section = TankMediaSection.Showcase, Sort = i });
            db.Context.MediaVariants.Add(new MediaVariant
            {
                MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/{marker}/{media.Id}.jpg",
            });
        }

        var post = new Post { AuthorProfileId = fixture.Seed.Alice.Id, TankId = tank.Id, Title = "Picture of the tank", State = PostState.Published };
        db.Context.Posts.Add(post);
        await db.Context.SaveChangesAsync();
        var postMedia = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo };
        db.Context.MediaItems.Add(postMedia);
        await db.Context.SaveChangesAsync();
        db.Context.PostMedia.Add(new PostMedia { PostId = post.Id, MediaItemId = postMedia.Id });
        db.Context.MediaVariants.Add(new MediaVariant
        {
            MediaItemId = postMedia.Id, Label = "large", StorageKey = $"variants/large/{marker}/post-{postMedia.Id}.jpg",
        });
        for (var i = 0; i < 12; i++)
        {
            db.Context.Ratings.Add(new Rating { TankId = tank.Id, Stars = 5 });
            db.Context.Ratings.Add(new Rating { PostId = post.Id, Stars = 5 });
        }

        await db.Context.SaveChangesAsync();
        return new Seeded(tank.Id, post.Id, marker);
    }

    private sealed class CichlidsDbContextAccessor(Cichlids.Infrastructure.Persistence.CichlidsDbContext context)
    {
        public Cichlids.Infrastructure.Persistence.CichlidsDbContext Context { get; } = context;
    }

    private DiscoverQueryService ServiceOn(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, IMemoryCache cache)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        return new DiscoverQueryService(db, new TankTimeLapseQueryService(db, store), store, cache, TimeProvider.System);
    }

    private static void AssertAbsent(DiscoverDto archive, Seeded seeded)
    {
        Assert.DoesNotContain(archive.Regions.SelectMany(r => r.Tanks), t => t.Id == seeded.Tank);
        Assert.DoesNotContain(archive.Regions.SelectMany(r => r.TimeLapses), t => t.Id == seeded.Tank);
        Assert.DoesNotContain(archive.Regions.Where(r => r.TankOfTheWeek != null), r => r.TankOfTheWeek!.Id == seeded.Tank);
        Assert.DoesNotContain(archive.Regions.SelectMany(r => r.Pictures), p => p.Id == seeded.Picture);
        Assert.DoesNotContain(archive.GlobalPictures, p => p.Id == seeded.Picture);
        var json = JsonSerializer.Serialize(archive, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(seeded.MediaMarker, json);
    }

    [Fact]
    public async Task GivenAPublicTank_WhenItIsRankedAndThenSwitchedToPrivate_ThenItLeavesEveryOutputAtOnceWhileTheCacheStays()
    {
        await using var db = fixture.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var seeded = await SeedTankAsync(new CichlidsDbContextAccessor(db), TankVisibility.Public, TankRegion.LakeVictoria);
        var service = ServiceOn(db, cache);

        var before = await service.GetAsync([TankRegion.LakeVictoria], default);
        var region = Assert.Single(before.Regions);
        Assert.Contains(region.Tanks, t => t.Id == seeded.Tank);
        Assert.Contains(region.TimeLapses, t => t.Id == seeded.Tank);
        Assert.Contains(region.Pictures, p => p.Id == seeded.Picture);
        Assert.Contains(before.GlobalPictures, p => p.Id == seeded.Picture);
        Assert.Contains(seeded.MediaMarker, JsonSerializer.Serialize(before));

        await db.Tanks.Where(t => t.Id == seeded.Tank)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private));
        var after = await service.GetAsync([TankRegion.LakeVictoria], default);

        AssertAbsent(after, seeded);
        Assert.Equal(1, cache.Count);
        await Cleanup(db, seeded);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenTheRankingsAreComputed_ThenTheTankItsPicturesAndItsMediaAreNotRanked()
    {
        await using var db = fixture.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var seeded = await SeedTankAsync(new CichlidsDbContextAccessor(db), TankVisibility.Private, TankRegion.LakeVictoria);

        var archive = await ServiceOn(db, cache).GetAsync(DiscoverQueryService.SelectableRegions, default);

        AssertAbsent(archive, seeded);
        await Cleanup(db, seeded);
    }

    [Fact]
    public async Task GivenAPublicProfileWithAPrivateStream_WhenTheRankingsAreComputed_ThenTheTankIsRanked()
    {
        await using var db = fixture.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var seeded = await SeedTankAsync(new CichlidsDbContextAccessor(db), TankVisibility.Public, TankRegion.LakeVictoria);
        await db.Tanks.Where(t => t.Id == seeded.Tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.StreamVisibility, TankVisibility.Private));

        var archive = await ServiceOn(db, cache).GetAsync([TankRegion.LakeVictoria], default);

        Assert.Contains(archive.Regions.Single().Tanks, t => t.Id == seeded.Tank);
        await Cleanup(db, seeded);
    }

    [Fact]
    public async Task GivenAPublicTankWhoseMediaStillLiveUnderThePrivatePrefix_WhenRanked_ThenNoPrivateUrlLeavesAndNothingThrows()
    {
        await using var db = fixture.CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var seeded = await SeedTankAsync(new CichlidsDbContextAccessor(db), TankVisibility.Public, TankRegion.LakeVictoria);
        await db.MediaVariants.Where(v => v.StorageKey.Contains(seeded.MediaMarker))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StorageKey, v => "private/" + v.StorageKey));

        var archive = await ServiceOn(db, cache).GetAsync([TankRegion.LakeVictoria], default);

        var json = JsonSerializer.Serialize(archive);
        Assert.DoesNotContain(seeded.MediaMarker, json);
        Assert.DoesNotContain("/api/tank-media/", json);
        await Cleanup(db, seeded);
    }

    private static async Task Cleanup(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, Seeded seeded)
    {
        await db.Ratings.Where(r => r.TankId == seeded.Tank || r.PostId == seeded.Picture).ExecuteDeleteAsync();
        await db.Posts.Where(p => p.Id == seeded.Picture).ExecuteDeleteAsync();
        await db.Tanks.Where(t => t.Id == seeded.Tank).ExecuteDeleteAsync();
    }
}
