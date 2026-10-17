// Story: TASK-8.3
using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The adoption of archive tanks outside the onboarding: the list of archive tanks, the suggested
/// photos in the order of their dates and the adoption with its links, event, badge and refusals.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-8.3")]
public sealed class AdoptArchiveTankTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _profileIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var ids = _profileIds.ToList();
        var tankIds = await db.Tanks.Where(t => ids.Contains(t.ProfileId)).Select(t => t.Id).ToListAsync();
        var tankKeys = tankIds.Select(t => t.ToString()).ToList();
        await db.OutboxEvents.Where(e => e.AggregateType == "tank" && tankKeys.Contains(e.AggregateId)).ExecuteDeleteAsync();
        await db.Stories.Where(s => ids.Contains(s.AuthorProfileId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => tankIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.MediaItems.Where(m => m.OwnerProfileId != null && ids.Contains(m.OwnerProfileId.Value)).ExecuteDeleteAsync();
    }

    private async Task<Member> NewTrackedMemberAsync()
    {
        var member = await NewMemberAsync(fixture);
        _profileIds.Add(member.ProfileId);
        return member;
    }

    private static int NewLegacyId() => Random.Shared.Next(1_500_000_000, int.MaxValue);

    private async Task<long> AddArchiveTankAsync(long ownerId, bool adopted = false)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var tank = new Tank
        {
            LegacyId = NewLegacyId(), ProfileId = ownerId, Title = $"Archive tank {Guid.NewGuid():N}", Category = TankCategory.Malawi,
            State = TankState.Published, CreatedAt = now, PublishedAt = now, AdoptedAt = adopted ? now : null,
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        return tank.Id;
    }

    private async Task<long> AddPhotoAsync(
        long ownerId, DateTime? takenLocal = null, DateTimeOffset? createdAt = null, bool migrated = true, MediaKind kind = MediaKind.Photo)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            LegacyId = migrated ? NewLegacyId() : null, OwnerProfileId = ownerId, Kind = kind, StorageKey = $"private/test/{Guid.NewGuid():N}.jpg",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
            TakenAtLocal = takenLocal, TakenAtOffsetMinutes = takenLocal is null ? null : (short)0,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaVariants.Add(new MediaVariant { MediaItemId = item.Id, Label = "medium", StorageKey = $"variants/medium/test-{item.Id}.jpg" });
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task AddStoryWithPhotoAsync(long authorId, long mediaItemId, DateTimeOffset publishedAt)
    {
        await using var db = fixture.CreateDbContext();
        var story = new Story
        {
            LegacyId = NewLegacyId(), AuthorProfileId = authorId, Topic = StoryTopic.Cichlids, Title = $"Story {Guid.NewGuid():N}",
            State = StoryState.Published, CreatedAt = publishedAt, UpdatedAt = publishedAt, PublishedAt = publishedAt,
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForStory(story.Id, mediaItemId));
        await db.SaveChangesAsync();
    }

    private static string Suggestions(long tankId, int? offset = null, int? limit = null) =>
        $"/api/me/legacy-tanks/{tankId}/photo-suggestions?offset={offset ?? 0}&limit={limit ?? 20}";

    private static string AdoptRoute(long tankId) => $"/api/me/legacy-tanks/{tankId}/adopt";

    private async Task<long[]> SuggestedIdsAsync(Member member, long tankId, int? offset = null, int? limit = null)
    {
        var response = await SendAsync(fixture, HttpMethod.Get, Suggestions(tankId, offset, limit), member.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("mediaItemId").GetInt64()).ToArray();
    }

    [Fact]
    public async Task SuggestsMigratedPhotosByCaptureDateWithStoryAndMediaItemFallbacks()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var captured2010 = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2010, 5, 1, 12, 0, 0));
        var inStory2008 = await AddPhotoAsync(member.ProfileId, createdAt: new DateTimeOffset(2012, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await AddStoryWithPhotoAsync(member.ProfileId, inStory2008, new DateTimeOffset(2008, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var created2012 = await AddPhotoAsync(member.ProfileId, createdAt: new DateTimeOffset(2012, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var captured2005 = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2005, 3, 1, 8, 0, 0), createdAt: new DateTimeOffset(2013, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // Not suggested: not migrated, a video, linked to the tank, a profile image, a photo of another member.
        await AddPhotoAsync(member.ProfileId, migrated: false);
        await AddPhotoAsync(member.ProfileId, kind: MediaKind.Video);
        var linked = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2001, 1, 1));
        var avatar = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2002, 1, 1));
        await AddPhotoAsync(other.ProfileId, takenLocal: new DateTime(2003, 1, 1));
        await using (var db = fixture.CreateDbContext())
        {
            db.MediaLinks.Add(MediaLink.ForTank(tank, linked, MediaLinkRole.Showcase));
            db.MediaLinks.Add(MediaLink.ForProfile(member.ProfileId, avatar, MediaLinkRole.Avatar));
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(fixture, HttpMethod.Get, Suggestions(tank), member.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(4, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([captured2005, inStory2008, captured2010, created2012], items.Select(i => i.GetProperty("mediaItemId").GetInt64()));
        Assert.Equal(JsonValueKind.String, items[0].GetProperty("date").ValueKind);
        Assert.Equal(JsonValueKind.String, items[0].GetProperty("takenAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("takenAt").ValueKind);
        Assert.Equal(2008, DateTimeOffset.Parse(items[1].GetProperty("date").GetString()!).Year);
        Assert.Equal(JsonValueKind.Object, items[0].GetProperty("image").ValueKind);
    }

    [Fact]
    public async Task PagesTheSuggestionsInOrder()
    {
        var member = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var ids = new List<long>();
        for (var i = 0; i < 5; i++) ids.Add(await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2010 + i, 1, 1)));

        Assert.Equal(ids.Take(2), await SuggestedIdsAsync(member, tank, 0, 2));
        Assert.Equal(ids.Skip(2).Take(2), await SuggestedIdsAsync(member, tank, 2, 2));
        Assert.Equal(ids.Skip(4), await SuggestedIdsAsync(member, tank, 4, 2));
        var page = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Suggestions(tank, 4, 2), member.Token));
        Assert.Equal(5, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SuggestionsAnswerOnlyForOpenArchiveTanksOfTheCaller()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var adopted = await AddArchiveTankAsync(member.ProfileId, adopted: true);
        var plain = await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);

        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Get, Suggestions(tank), other.Token), HttpStatusCode.NotFound, "legacy_tank_not_found");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Get, Suggestions(plain), member.Token), HttpStatusCode.NotFound, "legacy_tank_not_found");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Get, Suggestions(adopted), member.Token), HttpStatusCode.Conflict, "tank_already_adopted");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(fixture, HttpMethod.Get, Suggestions(tank))).StatusCode);
    }

    [Fact]
    public async Task AdoptionLinksTheChosenPhotosInOrderAndTheTankLeavesTheList()
    {
        var member = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var existing = await AddPhotoAsync(member.ProfileId);
        var first = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2011, 1, 1));
        var second = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2009, 1, 1));
        var third = await AddPhotoAsync(member.ProfileId, takenLocal: new DateTime(2010, 1, 1));
        await using (var db = fixture.CreateDbContext())
        {
            db.MediaLinks.Add(MediaLink.ForTank(tank, existing, MediaLinkRole.Showcase, 0));
            await db.SaveChangesAsync();
        }

        var before = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me/legacy-tanks", member.Token));
        Assert.Contains(before.EnumerateArray(), t => t.GetProperty("id").GetInt64() == tank);

        var response = await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { first, second, third } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await BodyAsync(response);
        Assert.Equal(tank, result.GetProperty("tankId").GetInt64());
        Assert.Equal("returned", result.GetProperty("badge").GetString());

        await using var check = fixture.CreateDbContext();
        var stored = await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.NotNull(stored.AdoptedAt);
        Assert.Equal(TankState.Published, stored.State);
        var links = await check.MediaLinks.Where(l => l.TankId == tank && l.Role == MediaLinkRole.Showcase).OrderBy(l => l.Position).ToListAsync();
        Assert.Equal([existing, first, second, third], links.Select(l => l.MediaItemId));
        Assert.Equal([0, 1, 2, 3], links.Select(l => l.Position));
        Assert.Single(await check.OutboxEvents.Where(e => e.EventType == "tank.adopted" && e.AggregateId == tank.ToString()).ToListAsync());
        Assert.Contains("returned", (await check.Profiles.AsNoTracking().SingleAsync(p => p.Id == member.ProfileId)).Badges);

        var after = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me/legacy-tanks", member.Token));
        Assert.DoesNotContain(after.EnumerateArray(), t => t.GetProperty("id").GetInt64() == tank);
    }

    [Fact]
    public async Task AdoptionWithoutPhotosAdoptsTheTank()
    {
        var member = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);

        var response = await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = Array.Empty<long>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var check = fixture.CreateDbContext();
        Assert.NotNull((await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).AdoptedAt);
        Assert.False(await check.MediaLinks.AnyAsync(l => l.TankId == tank));
    }

    [Fact]
    public async Task TheBadgeIsAwardedOnlyTheFirstTime()
    {
        var member = await NewTrackedMemberAsync();
        var firstTank = await AddArchiveTankAsync(member.ProfileId);
        var secondTank = await AddArchiveTankAsync(member.ProfileId);

        var first = await BodyAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(firstTank), member.Token, new { mediaItemIds = Array.Empty<long>() }));
        var second = await BodyAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(secondTank), member.Token, new { mediaItemIds = Array.Empty<long>() }));

        Assert.Equal("returned", first.GetProperty("badge").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("badge").ValueKind);
        await using var check = fixture.CreateDbContext();
        Assert.Equal(["returned"], (await check.Profiles.AsNoTracking().SingleAsync(p => p.Id == member.ProfileId)).Badges);
    }

    [Fact]
    public async Task ASecondAdoptionAnswersConflictAndLinksNothing()
    {
        var member = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var photo = await AddPhotoAsync(member.ProfileId);
        var other = await AddPhotoAsync(member.ProfileId);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { photo } })).StatusCode);

        await AssertProblemAsync(
            await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { other } }),
            HttpStatusCode.Conflict, "tank_already_adopted");

        await using var check = fixture.CreateDbContext();
        Assert.Equal([photo], await check.MediaLinks.Where(l => l.TankId == tank).Select(l => l.MediaItemId).ToListAsync());
        Assert.Single(await check.OutboxEvents.Where(e => e.EventType == "tank.adopted" && e.AggregateId == tank.ToString()).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentAdoptionsAdoptOnce()
    {
        var member = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var photo = await AddPhotoAsync(member.ProfileId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { photo } })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var check = fixture.CreateDbContext();
        Assert.Single(await check.MediaLinks.Where(l => l.TankId == tank).ToListAsync());
        Assert.Single(await check.OutboxEvents.Where(e => e.EventType == "tank.adopted" && e.AggregateId == tank.ToString()).ToListAsync());
    }

    [Fact]
    public async Task AMediaItemOfAnotherMemberRefusesTheAdoptionAndLinksNothing()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var own = await AddPhotoAsync(member.ProfileId);
        var foreign = await AddPhotoAsync(other.ProfileId);

        await AssertProblemAsync(
            await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { own, foreign } }),
            HttpStatusCode.NotFound, "media_not_found");

        await using var check = fixture.CreateDbContext();
        Assert.Null((await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).AdoptedAt);
        Assert.False(await check.MediaLinks.AnyAsync(l => l.TankId == tank));
        Assert.False(await check.OutboxEvents.AnyAsync(e => e.EventType == "tank.adopted" && e.AggregateId == tank.ToString()));
        Assert.DoesNotContain("returned", (await check.Profiles.AsNoTracking().SingleAsync(p => p.Id == member.ProfileId)).Badges);
    }

    [Fact]
    public async Task RefusesMissingDuplicateAndNonPhotoMediaItemsAndForeignTanks()
    {
        var member = await NewTrackedMemberAsync();
        var other = await NewTrackedMemberAsync();
        var tank = await AddArchiveTankAsync(member.ProfileId);
        var photo = await AddPhotoAsync(member.ProfileId);
        var video = await AddPhotoAsync(member.ProfileId, kind: MediaKind.Video);

        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { photo, photo } }),
            HttpStatusCode.BadRequest, "adoption_photos_invalid");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { 0L } }),
            HttpStatusCode.BadRequest, "adoption_photos_invalid");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { video } }),
            HttpStatusCode.NotFound, "media_not_found");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), member.Token, new { mediaItemIds = new[] { long.MaxValue } }),
            HttpStatusCode.NotFound, "media_not_found");
        await AssertProblemAsync(await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), other.Token, new { mediaItemIds = new[] { photo } }),
            HttpStatusCode.NotFound, "legacy_tank_not_found");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(fixture, HttpMethod.Post, AdoptRoute(tank), body: new { mediaItemIds = new[] { photo } })).StatusCode);

        await using var check = fixture.CreateDbContext();
        Assert.Null((await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).AdoptedAt);
    }

    [Fact]
    public async Task AnAdoptedArchiveTankStaysPublicAndTheOnboardingPathStillAdopts()
    {
        var member = await NewTrackedMemberAsync();
        var adoptedByRoute = await AddArchiveTankAsync(member.ProfileId);
        var adoptedByOnboarding = await AddArchiveTankAsync(member.ProfileId);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(fixture, HttpMethod.Post, AdoptRoute(adoptedByRoute), member.Token, new { mediaItemIds = Array.Empty<long>() })).StatusCode);

        var onboarding = await SendAsync(fixture, HttpMethod.Post, "/api/me/onboarding", member.Token, new { legacyTankId = adoptedByOnboarding });

        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        var list = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me/legacy-tanks", member.Token));
        Assert.Equal(0, list.GetArrayLength());
        var detail = await BodyAsync(await fixture.Client.GetAsync($"/api/tanks/{adoptedByRoute}"));
        Assert.Equal("public", detail.GetProperty("visibility").GetProperty("profile").GetString());
        await using var check = fixture.CreateDbContext();
        Assert.NotNull((await check.Tanks.AsNoTracking().SingleAsync(t => t.Id == adoptedByOnboarding)).AdoptedAt);
    }
}
