using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Discover;

[Collection("Discover")]
public class DiscoverTests(ApiFixture fixture)
{
    [Fact]
    public async Task AnonymousArchive_ReturnsSevenRegionsWithRatedTanksAndPictures()
    {
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Regional archive", Region = TankRegion.AmericasUnassigned, State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var story = new Story { AuthorProfileId = fixture.Seed.Alice.Id, Subjects = [StorySubject.ForTank(tank.Id)], Title = "Archive picture", State = StoryState.Published };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        var media = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo };
        db.MediaItems.Add(media);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForStory(story.Id, media.Id));
        for (var i = 0; i < 10; i++)
        {
            db.Ratings.Add(new Rating { TankId = tank.Id, Stars = 4 });
            db.Ratings.Add(new Rating { StoryId = story.Id, Stars = 5 });
        }
        await db.SaveChangesAsync();
        var response = await fixture.Client.GetAsync("/api/discover");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var regions = json.RootElement.GetProperty("regions").EnumerateArray().ToArray();
        Assert.Equal(7, regions.Length);
        foreach (var region in regions)
        {
            var american = region.GetProperty("region").GetString() is "central_america" or "south_america";
            Assert.Equal(american, region.GetProperty("tanks").EnumerateArray().Any(t => t.GetProperty("id").GetInt64() == tank.Id));
            Assert.Equal(american, region.GetProperty("pictures").EnumerateArray().Any(t => t.GetProperty("id").GetInt64() == story.Id));
            if (american) Assert.Equal(tank.Id, region.GetProperty("tankOfTheWeek").GetProperty("id").GetInt64());
        }
    }
    [Fact]
    public async Task Showcases_ReuseTheTankSeriesRuleAndSortByWeightedRatingWithoutTenVoteMinimum()
    {
        await using var db = fixture.CreateDbContext();
        using var scope = fixture.Services.CreateScope();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var objectStore = scope.ServiceProvider.GetRequiredService<Cichlids.Infrastructure.Storage.IObjectStore>();
        var timeLapses = new Cichlids.Api.Features.Tanks.TankTimeLapseQueryService(db, objectStore);
        var service = new Cichlids.Api.Features.Discover.DiscoverQueryService(db, timeLapses, objectStore, cache, TimeProvider.System);
        var expected = new List<long>();
        foreach (var (count, stars) in new[] { (8, 3), (8, 5), (7, 5) })
        {
            var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Showcase", Region = TankRegion.AfricaOther, State = TankState.Published };
            db.Tanks.Add(tank);
            await db.SaveChangesAsync();
            db.Ratings.Add(new Rating { TankId = tank.Id, Stars = (short)stars });
            for (var i = 0; i < count; i++)
            {
                var media = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo,
                    CreatedAt = DateTimeOffset.Parse("2020-01-01T00:00:00Z").AddMonths(i == count - 1 ? 12 : i) };
                db.MediaItems.Add(media);
                await db.SaveChangesAsync();
                db.MediaLinks.Add(MediaLink.ForTank(tank.Id, media.Id, MediaLinkRole.Showcase));
                db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/showcase/{media.Id}.jpg" });
            }
            await db.SaveChangesAsync();
            var single = await timeLapses.GetAsync(tank.Id, default);
            Assert.Equal(count == 8, single!.IsShowcase);
            if (single.IsShowcase) expected.Insert(0, tank.Id);
        }
        var response = Assert.Single((await service.GetAsync([TankRegion.AfricaOther], default)).Regions);
        Assert.Empty(response.Tanks);
        Assert.Equal(expected, response.TimeLapses.Select(t => t.Id));
        Assert.All(response.TimeLapses, t => Assert.NotNull(t.Image));
    }

    [Fact]
    public async Task Pictures_UseSpeciesWithoutTankTankPrecedenceAndCrossRegionHall()
    {
        await using var db = fixture.CreateDbContext();
        using var scope = fixture.Services.CreateScope();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var objectStore = scope.ServiceProvider.GetRequiredService<Cichlids.Infrastructure.Storage.IObjectStore>();
        var service = new Cichlids.Api.Features.Discover.DiscoverQueryService(db,
            new Cichlids.Api.Features.Tanks.TankTimeLapseQueryService(db, objectStore), objectStore, cache, TimeProvider.System);
        var tank = new Tank { ProfileId = fixture.Seed.Alice.Id, Title = "Tank precedence", Region = TankRegion.Mixed, State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var ids = new List<long>();
        foreach (var category in new string?[] { "lake malawi", "lake tanganyika", "african", "american", null, "american" })
        {
            var story = new Story { AuthorProfileId = fixture.Seed.Alice.Id, State = StoryState.Published,
                Subjects = ids.Count == 5 ? [StorySubject.ForTank(tank.Id)] : [], Title = $"Species fallback {ids.Count}" };
            var photo = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo };
            db.AddRange(story, photo);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForStory(story.Id, photo.Id));
            if (category != null)
            {
                var species = new Cichlids.Domain.Entities.Species { Genus = Guid.NewGuid().ToString(), Name = "test", Category = category };
                db.Species.Add(species);
                await db.SaveChangesAsync();
                db.StorySubjects.Add(new StorySubject { StoryId = story.Id, SubjectType = StorySubjectType.Species, SpeciesId = species.Id });
            }
            for (var i = 0; i < 100; i++) db.Ratings.Add(new Rating { StoryId = story.Id, Stars = 5 });
            await db.SaveChangesAsync();
            ids.Add(story.Id);
        }
        var archive = await service.GetAsync(Cichlids.Api.Features.Discover.DiscoverQueryService.SelectableRegions, default);
        var expected = new[] { TankRegion.LakeMalawi, TankRegion.LakeTanganyika, TankRegion.AfricaOther, TankRegion.CentralAmerica };
        for (var i = 0; i < expected.Length; i++)
            Assert.Contains(archive.Regions.Single(r => r.Region == expected[i]).Pictures, p => p.Id == ids[i]);
        Assert.Contains(archive.Regions.Single(r => r.Region == TankRegion.SouthAmerica).Pictures, p => p.Id == ids[3]);
        Assert.Contains(archive.Regions.Single(r => r.Region == TankRegion.Mixed).Pictures, p => p.Id == ids[5]);
        Assert.DoesNotContain(archive.Regions.Where(r => r.Region != TankRegion.Mixed).SelectMany(r => r.Pictures), p => p.Id == ids[5]);
        Assert.DoesNotContain(archive.Regions.SelectMany(r => r.Pictures), p => p.Id == ids[4]);
        var body = JsonSerializer.SerializeToElement(archive, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(body.TryGetProperty("globalPictures", out var global));
        Assert.InRange(global.GetArrayLength(), 6, 20);
        Assert.Contains(global.EnumerateArray(), p => p.GetProperty("id").GetInt64() == ids[4]);
    }

    [Fact]
    public async Task CachedArchive_HidesItemsThatBecameInvisibleAfterTheComputation()
    {
        await using var db = fixture.CreateDbContext();
        using var scope = fixture.Services.CreateScope();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var objectStore = scope.ServiceProvider.GetRequiredService<Cichlids.Infrastructure.Storage.IObjectStore>();
        var service = new Cichlids.Api.Features.Discover.DiscoverQueryService(db,
            new Cichlids.Api.Features.Tanks.TankTimeLapseQueryService(db, objectStore), objectStore, cache, TimeProvider.System);
        var suspended = new Profile { Username = Guid.NewGuid().ToString(), Kind = ProfileKind.Member };
        var active = new Profile { Username = Guid.NewGuid().ToString(), Kind = ProfileKind.Member };
        db.Profiles.AddRange(suspended, active);
        await db.SaveChangesAsync();
        var tank = new Tank { ProfileId = suspended.Id, Title = "Stale tank", Region = TankRegion.LakeVictoria, State = TankState.Published };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var stories = new List<Story>();
        foreach (var author in new[] { suspended, active })
        {
            var story = new Story { AuthorProfileId = author.Id, Subjects = [StorySubject.ForTank(tank.Id)], Title = "Stale picture", State = StoryState.Published };
            var photo = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo };
            db.AddRange(story, photo);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForStory(story.Id, photo.Id));
            for (var i = 0; i < 50; i++) db.Ratings.Add(new Rating { StoryId = story.Id, Stars = 5 });
            stories.Add(story);
        }
        for (var i = 0; i < 50; i++) db.Ratings.Add(new Rating { TankId = tank.Id, Stars = 5 });
        await db.SaveChangesAsync();
        try
        {
            await AssertCachedArchiveHidesAsync(db, service, suspended, tank, stories);
        }
        finally
        {
            var storyIds = stories.Select(p => p.Id).ToList();
            await db.Ratings.Where(r => r.TankId == tank.Id || (r.StoryId != null && storyIds.Contains(r.StoryId.Value))).ExecuteDeleteAsync();
            await db.MediaLinks.Where(l => l.StoryId != null && storyIds.Contains(l.StoryId.Value)).ExecuteDeleteAsync();
            await db.Stories.Where(p => storyIds.Contains(p.Id)).ExecuteDeleteAsync();
            await db.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync();
        }
    }

    private static async Task AssertCachedArchiveHidesAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext db,
        Cichlids.Api.Features.Discover.DiscoverQueryService service, Profile suspended, Tank tank, List<Story> stories)
    {
        var before = Assert.Single((await service.GetAsync([TankRegion.LakeVictoria], default)).Regions);
        Assert.Contains(before.Tanks, t => t.Id == tank.Id);
        Assert.Equal(tank.Id, before.TankOfTheWeek?.Id);
        Assert.Contains(before.Pictures, p => p.Id == stories[1].Id);

        suspended.SuspendedUntil = DateTimeOffset.UtcNow.AddDays(1);
        stories[1].DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var after = await service.GetAsync([TankRegion.LakeVictoria], default);
        var region = Assert.Single(after.Regions);
        Assert.DoesNotContain(region.Tanks, t => t.Id == tank.Id);
        Assert.NotEqual(tank.Id, region.TankOfTheWeek?.Id);
        Assert.DoesNotContain(region.TimeLapses, t => t.Id == tank.Id);
        Assert.DoesNotContain(region.Pictures, p => stories.Any(story => story.Id == p.Id));
        Assert.DoesNotContain(after.GlobalPictures, p => stories.Any(story => story.Id == p.Id));
    }

    [Theory]
    [InlineData("americas_unassigned")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task InvalidRegion_ReturnsCodedProblem(string region)
    {
        var response = await fixture.Client.GetAsync($"/api/discover?regions={region}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("discover_invalid_regions", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ranking_UsesEligibleRatingsGlobalPriorMinimumExclusionsAndTopTwenty()
    {
        await using var db = fixture.CreateDbContext();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2027-01-03T23:59:59Z"));
        using var scope = fixture.Services.CreateScope();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var objectStore = scope.ServiceProvider.GetRequiredService<Cichlids.Infrastructure.Storage.IObjectStore>();
        var service = new Cichlids.Api.Features.Discover.DiscoverQueryService(db,
            new Cichlids.Api.Features.Tanks.TankTimeLapseQueryService(db, objectStore), objectStore, cache, clock);
        var excluded = new List<long>();
        var excludedStories = new List<long>();
        async Task<Tank> Add(string title, int votes, short stars, ProfileKind kind = ProfileKind.Member,
            bool suspended = false, bool deleted = false, bool draft = false)
        {
            var owner = new Profile { Username = Guid.NewGuid().ToString(), Kind = kind,
                SuspendedUntil = suspended ? clock.GetUtcNow().AddDays(1) : null };
            db.Profiles.Add(owner);
            await db.SaveChangesAsync();
            var tank = new Tank { ProfileId = owner.Id, Title = title, Region = TankRegion.LakeVictoria,
                State = draft ? TankState.Draft : TankState.Published, DeletedAt = deleted ? clock.GetUtcNow() : null };
            db.Tanks.Add(tank);
            await db.SaveChangesAsync();
            var story = new Story { AuthorProfileId = owner.Id, Subjects = [StorySubject.ForTank(tank.Id)], Title = title,
                State = draft ? StoryState.Draft : StoryState.Published, DeletedAt = tank.DeletedAt, ViewCount = long.MaxValue };
            db.Stories.Add(story);
            var photo = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo };
            db.MediaItems.Add(photo);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForStory(story.Id, photo.Id));
            for (var i = 0; i < votes; i++)
            {
                db.Ratings.Add(new Rating { TankId = tank.Id, Stars = stars });
                db.Ratings.Add(new Rating { StoryId = story.Id, Stars = stars });
            }
            if (kind != ProfileKind.Member || suspended || deleted || draft || votes < 10)
            {
                excluded.Add(tank.Id);
                excludedStories.Add(story.Id);
            }
            await db.SaveChangesAsync();
            return tank;
        }
        await Add("Archived", 10, 5, ProfileKind.Archived);
        await Add("System", 10, 5, ProfileKind.System);
        await Add("Suspended", 10, 5, suspended: true);
        await Add("Deleted", 10, 5, deleted: true);
        await Add("Draft", 10, 5, draft: true);
        var low = await Add("Nine votes", 9, 5);
        db.Ratings.Add(new Rating { TankId = low.Id, ProfileId = low.ProfileId, Stars = 5 });
        var lowStory = await db.Stories.SingleAsync(p => p.Subjects.Any(s => s.TankId == low.Id));
        db.Ratings.Add(new Rating { StoryId = lowStory.Id, ProfileId = low.ProfileId, Stars = 5 });
        var best = await Add("Reliable favourite", 100, 5);
        await Add("Minimum", 10, 5);
        for (var i = 0; i < 21; i++) await Add($"Candidate {i}", 10, 3);
        var noRegion = new Tank { ProfileId = best.ProfileId, State = TankState.Published, Title = "Global prior" };
        db.Tanks.Add(noRegion);
        await db.SaveChangesAsync();
        for (var i = 0; i < 10; i++) db.Ratings.Add(new Rating { TankId = noRegion.Id, Stars = 1 });
        await db.SaveChangesAsync();
        var first = await service.GetAsync([TankRegion.LakeVictoria], default);
        Assert.DoesNotContain(first.GlobalPictures, p => excludedStories.Contains(p.Id));
        Assert.InRange(first.GlobalPictures.Count, 1, 20);
        var region = Assert.Single(first.Regions);
        Assert.Equal(20, region.Tanks.Count);
        Assert.Equal(20, region.Pictures.Count);
        Assert.Equal(best.Id, region.Tanks[0].Id);
        Assert.Equal("Reliable favourite", region.Pictures[0].Title);
        Assert.DoesNotContain(region.Tanks, t => excluded.Contains(t.Id));
        Assert.DoesNotContain(region.Pictures, p => excludedStories.Contains(p.Id));
        var eligible = db.Ratings.Where(r => r.TankId != null && db.Tanks.Any(t => t.Id == r.TankId && t.State == TankState.Published && t.DeletedAt == null
            && (r.ProfileId == null || r.ProfileId != t.ProfileId)
            && db.Profiles.Any(p => p.Id == t.ProfileId && p.Kind == ProfileKind.Member && p.SuspendedUntil == null)));
        var prior = await eligible.AverageAsync(r => (double)r.Stars);
        Assert.Equal((500 + 10 * prior) / 110, region.Tanks[0].Score, 10);
        var repeated = Assert.Single((await service.GetAsync([TankRegion.LakeVictoria, TankRegion.LakeVictoria], default)).Regions);
        Assert.Equal(region.Tanks, repeated.Tanks);
        Assert.Equal(region.Pictures, repeated.Pictures);
        Assert.Equal(region.TankOfTheWeek, repeated.TankOfTheWeek);
        clock.Advance(TimeSpan.FromSeconds(1));
        var next = Assert.Single((await service.GetAsync([TankRegion.LakeVictoria], default)).Regions);
        Assert.NotEqual(region.TankOfTheWeek!.Id, next.TankOfTheWeek!.Id);
        Assert.Empty(region.TimeLapses);
    }

}

[CollectionDefinition("Discover")]
public class DiscoverCollection : ICollectionFixture<ApiFixture>;
