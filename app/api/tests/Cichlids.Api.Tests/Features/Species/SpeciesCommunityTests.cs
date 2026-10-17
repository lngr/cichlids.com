using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Species;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Species;

/// <summary>
/// The community side of a species page: the best photos, the public tanks keeping the species and the
/// stocking partners found in at least three of them. Only tanks anyone may see count, and only those of
/// members who are not suspended.
/// </summary>
[Collection("SpeciesCommunity")]
public sealed class SpeciesCommunityTests(ApiFixture fixture)
{
    private static int _counter;

    private static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _counter)}x{Guid.NewGuid().ToString("N")[..6]}";

    private async Task<Cichlids.Domain.Entities.Species> AddSpeciesAsync(CichlidsDbContext db)
    {
        var token = Unique("Genus");
        var species = new Cichlids.Domain.Entities.Species
        {
            Genus = token, Name = "alba", DisplayName = $"{token} alba", Slug = $"{token.ToLowerInvariant()}-alba", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Species.Add(species);
        await db.SaveChangesAsync();
        return species;
    }

    private async Task<Profile> AddProfileAsync(
        CichlidsDbContext db, ProfileKind kind = ProfileKind.Member, DateTimeOffset? suspendedUntil = null)
    {
        var profile = new Profile
        {
            Username = Unique("owner"), DisplayName = "Owner", Kind = kind, SuspendedUntil = suspendedUntil, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private async Task<Tank> AddTankAsync(
        CichlidsDbContext db, long? ownerId, IEnumerable<long> speciesIds, TankVisibility visibility = TankVisibility.Public,
        TankState state = TankState.Published, bool deleted = false, string? title = null, DateTimeOffset? publishedAt = null)
    {
        var tank = new Tank
        {
            ProfileId = ownerId ?? fixture.Seed.Alice.Id, Title = title ?? Unique("Tank"), State = state,
            ProfileVisibility = visibility, StreamVisibility = visibility, CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = publishedAt ?? DateTimeOffset.UtcNow, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
            Inhabitants = speciesIds.Select((id, index) => new Inhabitant { SpeciesId = id, OwnerProfileId = ownerId ?? fixture.Seed.Alice.Id, Count = 3, Sort = index }).ToList(),
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        return tank;
    }

    private async Task<Story> AddStoryAsync(
        CichlidsDbContext db, long speciesId, long? authorId = null, long views = 0, double? average = null, int ratings = 0,
        StoryState state = StoryState.Published, bool deleted = false, long? tankId = null, string? title = null)
    {
        var story = new Story
        {
            AuthorProfileId = authorId ?? fixture.Seed.Alice.Id, Topic = StoryTopic.Cichlids, Title = title ?? Unique("Story"), State = state,
            ViewCount = views, RatingAverage = average, RatingCount = ratings, CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
            Subjects = [StorySubject.ForSpecies(speciesId)],
        };
        if (tankId is { } tank)
        {
            story.Subjects.Add(StorySubject.ForTank(tank));
        }

        db.Stories.Add(story);
        await db.SaveChangesAsync();
        db.SlugAliases.Add(new SlugAlias { StoryId = story.Id, Value = Unique("slug"), IsCanonical = true, CreatedAt = DateTimeOffset.UtcNow });
        var media = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, CreatedAt = DateTimeOffset.UtcNow };
        db.MediaItems.Add(media);
        await db.SaveChangesAsync();
        db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "large", StorageKey = $"variants/large/community/{media.Id}.jpg" });
        db.MediaLinks.Add(MediaLink.ForStory(story.Id, media.Id));
        await db.SaveChangesAsync();
        return story;
    }

    private async Task<SpeciesCommunityDto> GetAsync(string idOrSlug)
    {
        using var response = await fixture.Client.GetAsync($"/api/species/{idOrSlug}/community");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<SpeciesCommunityDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    [Fact]
    public async Task GivenRatedStoriesAndTanks_WhenAVisitorAsks_ThenItGetsThePhotosTheTankCountAndThePartnersWithThreeTanks()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var partner = await AddSpeciesAsync(db);
        var rare = await AddSpeciesAsync(db);
        var best = await AddStoryAsync(db, species.Id, views: 5, average: 5.0, ratings: 400);
        var weak = await AddStoryAsync(db, species.Id, views: 900, average: 3.0, ratings: 400);
        for (var i = 0; i < 3; i++)
        {
            await AddTankAsync(db, null, [species.Id, partner.Id]);
        }

        await AddTankAsync(db, null, [species.Id, rare.Id]);
        await AddTankAsync(db, null, [species.Id, rare.Id]);

        var community = await GetAsync(species.Slug!);

        Assert.Equal([best.Id, weak.Id], community.Photos.Select(p => p.StoryId));
        Assert.NotNull(community.Photos[0].Image.Large);
        Assert.Equal(5, community.TankCount);
        Assert.Equal(5, community.Tanks.Count);
        var only = Assert.Single(community.StockingPartners);
        Assert.Equal(partner.Id, only.Species.Id);
        Assert.Equal(3, only.TankCount);
    }

    [Fact]
    public async Task GivenTanksThatAnonymousViewersDoNotSee_WhenAVisitorAsks_ThenNoneOfThemCountsAnywhere()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var partner = await AddSpeciesAsync(db);
        var suspended = await AddProfileAsync(db, suspendedUntil: DateTimeOffset.UtcNow.AddDays(3));
        var provisional = await AddProfileAsync(db);
        var visible = await AddTankAsync(db, null, [species.Id, partner.Id]);
        var hidden = new List<Tank>
        {
            await AddTankAsync(db, null, [species.Id, partner.Id], TankVisibility.Private),
            await AddTankAsync(db, null, [species.Id, partner.Id], TankVisibility.Followers),
            await AddTankAsync(db, null, [species.Id, partner.Id], state: TankState.Draft),
            await AddTankAsync(db, null, [species.Id, partner.Id], deleted: true),
            await AddTankAsync(db, suspended.Id, [species.Id, partner.Id]),
            await AddTankAsync(db, provisional.Id, [species.Id, partner.Id]),
        };
        await db.Profiles.Where(p => p.Id == provisional.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Kind, ProfileKind.Provisional));

        var community = await GetAsync(species.Slug!);

        Assert.Equal(1, community.TankCount);
        Assert.Equal([visible.Id], community.Tanks.Select(t => t.Id));
        Assert.Empty(community.StockingPartners);
        var json = JsonSerializer.Serialize(community);
        Assert.All(hidden, tank => Assert.DoesNotContain(tank.Title, json));
    }

    [Fact]
    public async Task GivenAPartnerInTwoPublicTanksAndAPrivateOne_WhenAVisitorAsks_ThenThePartnerStaysHiddenUntilThreePublicTanks()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var partner = await AddSpeciesAsync(db);
        await AddTankAsync(db, null, [species.Id, partner.Id]);
        await AddTankAsync(db, null, [species.Id, partner.Id]);
        await AddTankAsync(db, null, [species.Id, partner.Id], TankVisibility.Private);

        Assert.Empty((await GetAsync(species.Slug!)).StockingPartners);

        await AddTankAsync(db, null, [species.Id, partner.Id]);

        var partners = (await GetAsync(species.Slug!)).StockingPartners;
        Assert.Equal(3, Assert.Single(partners).TankCount);
    }

    [Fact]
    public async Task GivenARemovedInhabitant_WhenAVisitorAsks_ThenItCountsNeitherForTheTankCountNorForThePartners()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var partner = await AddSpeciesAsync(db);
        var kept = new List<Tank>();
        for (var i = 0; i < 4; i++)
        {
            kept.Add(await AddTankAsync(db, null, [species.Id, partner.Id]));
        }

        var gone = await AddTankAsync(db, null, [species.Id, partner.Id]);
        await db.Inhabitants.Where(i => i.TankId == gone.Id && i.SpeciesId == species.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RemovedOn, new DateOnly(2026, 10, 1)).SetProperty(i => i.RemovalReason, Cichlids.Domain.Enums.InhabitantRemovalReason.Died));
        await db.Inhabitants.Where(i => i.TankId == kept[0].Id && i.SpeciesId == partner.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RemovedOn, new DateOnly(2026, 10, 1)).SetProperty(i => i.RemovalReason, Cichlids.Domain.Enums.InhabitantRemovalReason.GivenAway));

        var community = await GetAsync(species.Slug!);

        Assert.Equal(4, community.TankCount);
        Assert.DoesNotContain(gone.Id, community.Tanks.Select(t => t.Id));
        Assert.Equal(3, Assert.Single(community.StockingPartners).TankCount);
    }

    [Fact]
    public async Task GivenSeveralPartners_WhenAVisitorAsks_ThenTheyAreOrderedByTankCountAndCountEachTankOnce()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var many = await AddSpeciesAsync(db);
        var few = await AddSpeciesAsync(db);
        for (var i = 0; i < 4; i++)
        {
            await AddTankAsync(db, null, [species.Id, many.Id, many.Id]);
        }

        for (var i = 0; i < 3; i++)
        {
            await AddTankAsync(db, null, [species.Id, few.Id]);
        }

        var partners = (await GetAsync(species.Slug!)).StockingPartners;

        Assert.Equal([many.Id, few.Id], partners.Select(p => p.Species.Id));
        Assert.Equal([4, 3], partners.Select(p => p.TankCount));
    }

    [Fact]
    public async Task GivenMoreThanTwelveStories_WhenAVisitorAsks_ThenThereAreTwelvePhotosOrderedByRatingThenViews()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var expected = new List<long>();
        for (var i = 0; i < 14; i++)
        {
            var story = await AddStoryAsync(db, species.Id, views: i, average: 4.0, ratings: 50);
            expected.Insert(0, story.Id);
        }

        var photos = (await GetAsync(species.Slug!)).Photos;

        Assert.Equal(12, photos.Count);
        Assert.Equal(expected.Take(12), photos.Select(p => p.StoryId));
    }

    [Fact]
    public async Task GivenStoriesThatAreNotPublic_WhenAVisitorAsks_ThenTheyAreNotAmongThePhotos()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var suspended = await AddProfileAsync(db, suspendedUntil: DateTimeOffset.UtcNow.AddDays(3));
        var privateTank = await AddTankAsync(db, null, [species.Id], TankVisibility.Private);
        var shown = await AddStoryAsync(db, species.Id);
        await AddStoryAsync(db, species.Id, state: StoryState.Draft);
        await AddStoryAsync(db, species.Id, deleted: true);
        await AddStoryAsync(db, species.Id, authorId: suspended.Id);
        await AddStoryAsync(db, species.Id, tankId: privateTank.Id);

        var photos = (await GetAsync(species.Slug!)).Photos;

        Assert.Equal([shown.Id], photos.Select(p => p.StoryId));
    }

    [Fact]
    public async Task GivenManyTanks_WhenAVisitorAsks_ThenTheCountIsCompleteAndTheCardsAreTheNewestSix()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);
        var ids = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            ids.Insert(0, (await AddTankAsync(db, null, [species.Id], publishedAt: DateTimeOffset.UtcNow.AddHours(-20 + i))).Id);
        }

        var community = await GetAsync(species.Id.ToString());

        Assert.Equal(8, community.TankCount);
        Assert.Equal(ids.Take(6), community.Tanks.Select(t => t.Id));
    }

    [Fact]
    public async Task GivenASpeciesWithoutCommunityData_WhenAVisitorAsks_ThenTheSectionsAreEmpty()
    {
        await using var db = fixture.CreateDbContext();
        var species = await AddSpeciesAsync(db);

        var community = await GetAsync(species.Slug!);

        Assert.Empty(community.Photos);
        Assert.Equal(0, community.TankCount);
        Assert.Empty(community.Tanks);
        Assert.Empty(community.StockingPartners);
    }

    [Fact]
    public async Task GivenAnUnknownSpecies_WhenAVisitorAsks_ThenItIsNotFound()
    {
        using var response = await fixture.Client.GetAsync("/api/species/no-such-species/community");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[CollectionDefinition("SpeciesCommunity")]
public sealed class SpeciesCommunityCollection : ICollectionFixture<ApiFixture>;
