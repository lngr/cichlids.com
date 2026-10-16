using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Discover;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Api.Tests.Features.Me;

[Collection("Today")]
public class TodayTests(ApiFixture fixture)
{
    [Fact]
    public async Task RequiresAuthentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync("/api/me/today?date=2026-10-15")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-02-29")]
    [InlineData("2026-2-01")]
    [InlineData("2026-10-15T00:00:00Z")]
    [InlineData("garbage")]
    [InlineData("2026-10-15 ")]
    public async Task RejectsInvalidLocalDate(string date)
    {
        var (_, token) = await MemberAsync();
        using var response = await SendAsync(token, date);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("today_invalid_date", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LegacyGreeting_OwnPublishedTanksNewestFirstWithNewestPhoto_AndLimitedDrafts()
    {
        var (member, token) = await MemberAsync(true);
        await using var db = fixture.CreateDbContext();
        var tanks = Enumerable.Range(1, 12).Select(i => new Tank { ProfileId = member.Id, Title = $"Tank {i}", State = TankState.Published, CreatedAt = Date(2020).AddDays(i) }).ToArray();
        db.Tanks.AddRange(tanks);
        db.Tanks.AddRange(new Tank { ProfileId = member.Id, Title = "Private", State = TankState.Draft },
            new Tank { ProfileId = member.Id, Title = "Deleted", State = TankState.Published, DeletedAt = Date(2025) });
        await db.SaveChangesAsync();
        var older = await PhotoAsync(member.Id, Date(2020), tanks[^1].Id);
        var newest = await PhotoAsync(member.Id, Date(2021), tanks[^1].Id);
        for (var i = 0; i < 5; i++) await PhotoAsync(member.Id, Date(2025).AddDays(i), state: StoryState.Draft);
        await PhotoAsync(member.Id, Date(2025), state: StoryState.Draft, deleted: true);
        await PhotoAsync(fixture.Seed.Bob.Id, Date(2025), state: StoryState.Draft);
        using var json = await GetAsync(token);
        var root = json.RootElement;
        Assert.Equal(member.DisplayName, root.GetProperty("displayName").GetString());
        Assert.Equal(2006, root.GetProperty("memberSinceYear").GetInt32());
        var cards = root.GetProperty("tanks").EnumerateArray().ToArray();
        Assert.Equal(tanks.Reverse().Take(10).Select(t => t.Id), cards.Select(t => t.GetProperty("id").GetInt64()));
        Assert.Equal(newest.MediaId, cards[0].GetProperty("newestPhoto").GetProperty("mediaItemId").GetInt64());
        Assert.NotEqual(older.MediaId, newest.MediaId);
        Assert.EndsWith($"/variants/{newest.MediaId}.jpg", cards[0].GetProperty("newestPhoto").GetProperty("image").GetProperty("medium").GetString());
        Assert.Equal(5, root.GetProperty("draftCount").GetInt32());
        var drafts = root.GetProperty("drafts").EnumerateArray().ToArray();
        Assert.Equal(3, drafts.Length);
        Assert.True(drafts[0].GetProperty("createdAt").GetDateTimeOffset() > drafts[1].GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task NewMemberHasNoLegacyYearOrEmptyPersonalSections()
    {
        var (_, token) = await MemberAsync();
        using var json = await GetAsync(token);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("memberSinceYear").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("tanks").EnumerateArray());
        Assert.Equal(0, json.RootElement.GetProperty("draftCount").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("drafts").EnumerateArray());
        Assert.False(json.RootElement.TryGetProperty("onThisDay", out _));
    }

    [Fact]
    public async Task AnniversaryUsesMostRecentEarlierYearAndOnlyOwnPublishedPhotos()
    {
        var (member, token) = await MemberAsync(true);
        await PhotoAsync(member.Id, Date(2018));
        var expected = await PhotoAsync(member.Id, Date(2020));
        await PhotoAsync(fixture.Seed.Bob.Id, Date(2025));
        await PhotoAsync(member.Id, Date(2026));
        await PhotoAsync(member.Id, Date(2024), state: StoryState.Draft);
        await PhotoAsync(member.Id, Date(2023), deleted: true);
        await PhotoAsync(member.Id, Date(2025), kind: MediaKind.Video);
        await PhotoAsync(member.Id, Date(2025).AddDays(-1));
        using var json = await GetAsync(token);
        var memory = json.RootElement.GetProperty("onThisDay");
        Assert.Equal(expected.StoryId, memory.GetProperty("storyId").GetInt64());
        Assert.Equal(6, memory.GetProperty("yearsAgo").GetInt32());
    }

    [Fact]
    public async Task AnniversaryUsesTheCaptureDateOfThePhotoInsteadOfThePublicationDate()
    {
        var (member, token) = await MemberAsync(true);
        var captured = await PhotoAsync(member.Id, new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero), takenAtLocal: new DateTime(2021, 10, 15, 23, 40, 0));
        using var json = await GetAsync(token);
        var memory = json.RootElement.GetProperty("onThisDay");
        Assert.Equal(captured.StoryId, memory.GetProperty("storyId").GetInt64());
        Assert.Equal(5, memory.GetProperty("yearsAgo").GetInt32());
    }

    [Fact]
    public async Task AnniversaryIgnoresAPhotoPublishedOnTheDayWhenItWasCapturedOnAnotherDay()
    {
        var (member, token) = await MemberAsync(true);
        await PhotoAsync(member.Id, Date(2020), takenAtLocal: new DateTime(2019, 6, 1, 8, 0, 0));
        using var json = await GetAsync(token);
        Assert.False(json.RootElement.TryGetProperty("onThisDay", out _));
    }

    [Fact]
    public async Task LeapDayOnlyMatchesFebruary29()
    {
        var (member, token) = await MemberAsync(true);
        await PhotoAsync(member.Id, new DateTimeOffset(2020, 2, 29, 12, 0, 0, TimeSpan.Zero));
        foreach (var date in new[] { "2026-02-28", "2026-03-01" })
        {
            using var empty = await GetAsync(token, date);
            Assert.False(empty.RootElement.TryGetProperty("onThisDay", out _));
        }
        using var match = await GetAsync(token, "2024-02-29");
        Assert.Equal(4, match.RootElement.GetProperty("onThisDay").GetProperty("yearsAgo").GetInt32());
    }

    [Fact]
    public async Task AnniversaryExcludesAnotherMembersMediaLinkedToAnOwnStory()
    {
        var (member, token) = await MemberAsync(true);
        var foreign = await PhotoAsync(fixture.Seed.Bob.Id, Date(2025));
        await using var db = fixture.CreateDbContext();
        var story = new Story { AuthorProfileId = member.Id, State = StoryState.Published, PublishedAt = Date(2025), CreatedAt = Date(2025) };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForStory(story.Id, foreign.MediaId));
        await db.SaveChangesAsync();
        using var json = await GetAsync(token);
        Assert.False(json.RootElement.TryGetProperty("onThisDay", out _));
    }

    [Theory]
    [InlineData("south_america", "lake_malawi", 2)]
    [InlineData("lake_malawi", "south_america", 1)]
    [InlineData("lake_victoria", "south_america", 2)]
    [InlineData("lake_victoria", "mixed", 1)]
    public async Task WeeklyTankUsesFirstInterestWithContentThenSelectableRegionOrder(string first, string second, int expected)
    {
        var (member, token) = await MemberAsync();
        await using var db = fixture.CreateDbContext();
        var profile = await db.Profiles.FindAsync(member.Id);
        profile!.Interests = [first, second];
        await db.SaveChangesAsync();
        var malawi = new Tank { ProfileId = member.Id, Title = "Cached Malawi", Region = TankRegion.LakeMalawi, State = TankState.Published };
        var america = new Tank { ProfileId = member.Id, Title = "Cached America", Region = TankRegion.SouthAmerica, State = TankState.Published };
        db.Tanks.AddRange(malawi, america);
        await db.SaveChangesAsync();
        var cache = fixture.Services.GetRequiredService<IMemoryCache>();
        var now = fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var key = $"discover:{System.Globalization.ISOWeek.GetYear(now)}:{System.Globalization.ISOWeek.GetWeekOfYear(now)}";
        var archive = new DiscoverDto(DiscoverQueryService.SelectableRegions.Select(r =>
        {
            var tank = r == TankRegion.LakeMalawi ? malawi : r == TankRegion.SouthAmerica ? america : null;
            DiscoverItemDto? card = tank == null ? null : new(tank.Id, tank.Title, null, null, 4, 10);
            return new DiscoverRegionDto(r, card == null ? [] : [card], [], card, []);
        }).ToList(), []);
        cache.Set(key, archive);
        try
        {
            using var json = await GetAsync(token);
            Assert.Equal(expected == 1 ? malawi.Id : america.Id, json.RootElement.GetProperty("tankOfTheWeek").GetProperty("tank").GetProperty("id").GetInt64());
            Assert.Same(archive, cache.Get<DiscoverDto>(key));
        }
        finally { cache.Remove(key); }
    }

    private static DateTimeOffset Date(int year) => new(year, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private async Task<(Profile Profile, string Token)> MemberAsync(bool legacy = false)
    {
        await using var db = fixture.CreateDbContext();
        var subject = Guid.NewGuid().ToString();
        var profile = new Profile { Username = subject, DisplayName = "Today Member", CreatedAt = Date(2006), LegacyId = legacy ? Random.Shared.Next(100000, int.MaxValue) : null,
            Identities = [new ProfileIdentity { Provider = "oidc", Subject = subject, CreatedAt = Date(2006) }] };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return (profile, TestTokens.Create(subject, subject));
    }

    private async Task<(long StoryId, long MediaId)> PhotoAsync(long owner, DateTimeOffset date, long? tank = null, StoryState state = StoryState.Published, bool deleted = false, MediaKind kind = MediaKind.Photo, DateTime? takenAtLocal = null)
    {
        await using var db = fixture.CreateDbContext();
        var media = new MediaItem { OwnerProfileId = owner, Kind = kind, StorageKey = Guid.NewGuid().ToString(), CreatedAt = date, TakenAtLocal = takenAtLocal };
        var story = new Story { AuthorProfileId = owner, Subjects = tank is { } tankId ? [StorySubject.ForTank(tankId)] : [], Title = "Today photo", State = state, CreatedAt = date, UpdatedAt = date, PublishedAt = state == StoryState.Published ? date : null, DeletedAt = deleted ? date : null };
        db.AddRange(media, story);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForStory(story.Id, media.Id));
        db.MediaVariants.Add(new MediaVariant { MediaItemId = media.Id, Label = "medium", Width = 800, StorageKey = $"variants/{media.Id}.jpg" });
        db.SlugAliases.Add(new SlugAlias { StoryId = story.Id, Value = $"today-{story.Id}", IsCanonical = true, CreatedAt = date });
        await db.SaveChangesAsync();
        return (story.Id, media.Id);
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string date)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/me/today?date={Uri.EscapeDataString(date)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private async Task<JsonDocument> GetAsync(string token, string date = "2026-10-15")
    {
        using var response = await SendAsync(token, date);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}

[CollectionDefinition("Today")]
public sealed class TodayCollection : ICollectionFixture<ApiFixture>;
