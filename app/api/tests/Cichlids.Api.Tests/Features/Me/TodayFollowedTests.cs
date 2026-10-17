using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The followed tanks section of Today: the newest visible update of each followed tank within 30 days,
/// at most five, and nothing of what the member may not see.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.24")]
public sealed class TodayFollowedTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _stories = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Stories.Where(s => _stories.Contains(s.Id)).ExecuteDeleteAsync();
        var tanks = db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && (p.Username.StartsWith("vis-") || p.Username.StartsWith("prov-"))));
        await db.Follows.Where(f => tanks.Any(t => t.Id == f.TargetTankId)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(m => tanks.Any(t => t.Id == m.TankId)).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => tanks.Any(t => t.Id == e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && p.Username.StartsWith("prov-"))).ExecuteDeleteAsync();
        await RemoveEventsAsync(fixture, "streak_freeze");
    }

    private static DateTimeOffset Ago(int days, int hours = 0) => DateTimeOffset.UtcNow.AddDays(-days).AddHours(-hours);

    private async Task FollowAsync(Member viewer, long tank, FollowStatus status = FollowStatus.Approved)
    {
        await using var db = fixture.CreateDbContext();
        db.Follows.Add(new Follow
        {
            FollowerProfileId = viewer.ProfileId, TargetTankId = tank, Status = status, CreatedAt = DateTimeOffset.UtcNow, DecidedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<long> AddStoryAsync(long author, long tank, DateTimeOffset published, StoryState state = StoryState.Published, string title = "A story")
    {
        await using var db = fixture.CreateDbContext();
        var story = new Story
        {
            AuthorProfileId = author, Title = title, State = state, CreatedAt = published, UpdatedAt = published, PublishedAt = published,
            Subjects = [StorySubject.ForTank(tank)],
        };
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        _stories.Add(story.Id);
        return story.Id;
    }

    private async Task<long> AddPhotoAsync(long tank, long owner, MediaLinkRole role, DateTimeOffset at)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = owner, CreatedAt = at, ReceivedAt = at, CapturedInApp = true,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        var link = MediaLink.ForTank(tank, item.Id, role);
        link.CreatedAt = at;
        db.MediaLinks.Add(link);
        db.MediaVariants.Add(new MediaVariant { MediaItemId = item.Id, Label = "thumb", StorageKey = $"variants/thumb/{item.StorageKey}.jpg", Width = 200 });
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<long> AddCareEntryAsync(long tank, long author, DateTimeOffset at, bool onlyMe = false, CareKind kind = CareKind.Measurement)
    {
        await using var db = fixture.CreateDbContext();
        var entry = new CareEntry { TankId = tank, AuthorProfileId = author, Kind = kind, OccurredAt = at, RecordedAt = at, OnlyMe = onlyMe };
        db.CareEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<JsonElement> TodayAsync(Member viewer)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, "/api/me/today/followed", viewer.Token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    private static long[] Ids(JsonElement body) => body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tankId").GetInt64()).ToArray();

    private async Task<(Member Owner, long Tank)> PublicTankAsync(string? title = null, TankVisibility profile = TankVisibility.Public,
        TankVisibility stream = TankVisibility.Public)
    {
        var owner = await NewMemberAsync(fixture);
        return (owner, await NewTankAsync(fixture, owner.ProfileId, profile, stream, title));
    }

    [Fact]
    public async Task GivenFollowedTanksWithFreshUpdates_WhenTodayLoads_ThenTheyAreListedByTheirNewestUpdateWithThatUpdate()
    {
        var viewer = await NewMemberAsync(fixture);
        var (storyOwner, withStory) = await PublicTankAsync("Story tank");
        var (photoOwner, withPhoto) = await PublicTankAsync("Photo tank");
        var (careOwner, withCare) = await PublicTankAsync("Care tank");
        var (_, quiet) = await PublicTankAsync("Quiet tank");
        foreach (var tank in new[] { withStory, withPhoto, withCare, quiet }) await FollowAsync(viewer, tank);
        var story = await AddStoryAsync(storyOwner.ProfileId, withStory, Ago(1), title: "Spawning");
        await AddStoryAsync(storyOwner.ProfileId, withStory, Ago(9), title: "Older story");
        await AddPhotoAsync(withPhoto, photoOwner.ProfileId, MediaLinkRole.TimeLapse, Ago(2));
        await AddCareEntryAsync(withCare, careOwner.ProfileId, Ago(3), kind: CareKind.WaterChange);
        await AddCareEntryAsync(withCare, careOwner.ProfileId, Ago(8), kind: CareKind.Measurement);

        var body = await TodayAsync(viewer);

        Assert.Equal(new[] { withStory, withPhoto, withCare }, Ids(body));
        Assert.False(body.GetProperty("hasMore").GetBoolean());
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal("story", items[0].GetProperty("update").GetProperty("kind").GetString());
        Assert.Equal(story, items[0].GetProperty("update").GetProperty("story").GetProperty("id").GetInt64());
        Assert.Equal("Spawning", items[0].GetProperty("update").GetProperty("story").GetProperty("title").GetString());
        Assert.Equal("Story tank", items[0].GetProperty("title").GetString());
        Assert.Equal(storyOwner.ProfileId, items[0].GetProperty("owner").GetProperty("id").GetInt64());
        Assert.Equal("time_lapse_photo", items[1].GetProperty("update").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Object, items[1].GetProperty("update").GetProperty("image").ValueKind);
        Assert.Equal("care_entry", items[2].GetProperty("update").GetProperty("kind").GetString());
        Assert.Equal("water_change", items[2].GetProperty("update").GetProperty("careKind").GetString());
    }

    [Fact]
    public async Task GivenTankPhotosOfSections_WhenTodayLoads_ThenTheyCountAsTankPhotos()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, tank) = await PublicTankAsync();
        await FollowAsync(viewer, tank);
        await AddPhotoAsync(tank, owner.ProfileId, MediaLinkRole.Showcase, Ago(1));

        var item = Assert.Single((await TodayAsync(viewer)).GetProperty("items").EnumerateArray());

        Assert.Equal("tank_photo", item.GetProperty("update").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GivenAnUpdateOlderThanThirtyDays_WhenTodayLoads_ThenTheTankIsNotShown()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, stale) = await PublicTankAsync();
        var (_, edge) = await PublicTankAsync();
        await FollowAsync(viewer, stale);
        await FollowAsync(viewer, edge);
        await AddStoryAsync(owner.ProfileId, stale, Ago(31));
        await AddStoryAsync(owner.ProfileId, edge, Ago(29));

        Assert.Equal(new[] { edge }, Ids(await TodayAsync(viewer)));
    }

    [Fact]
    public async Task GivenMoreThanFiveTanksWithUpdates_WhenTodayLoads_ThenTheFiveNewestShowAndMoreIsFlagged()
    {
        var viewer = await NewMemberAsync(fixture);
        var tanks = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var (owner, tank) = await PublicTankAsync();
            await FollowAsync(viewer, tank);
            await AddStoryAsync(owner.ProfileId, tank, Ago(i + 1));
            tanks.Add(tank);
        }

        var body = await TodayAsync(viewer);

        Assert.Equal(tanks.Take(5).ToArray(), Ids(body));
        Assert.True(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenExactlyFiveTanks_WhenTodayLoads_ThenNothingMoreIsFlagged()
    {
        var viewer = await NewMemberAsync(fixture);
        for (var i = 0; i < 5; i++)
        {
            var (owner, tank) = await PublicTankAsync();
            await FollowAsync(viewer, tank);
            await AddStoryAsync(owner.ProfileId, tank, Ago(1));
        }

        var body = await TodayAsync(viewer);

        Assert.Equal(5, Ids(body).Length);
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenPrivateCareEntries_WhenTodayLoads_ThenNeitherTheEntryNorAHintOfItAppears()
    {
        var viewer = await NewMemberAsync(fixture);
        var (onlyMeOwner, onlyMeTank) = await PublicTankAsync();
        var (streamOwner, privateStreamTank) = await PublicTankAsync(stream: TankVisibility.Private);
        var (mixedOwner, mixedTank) = await PublicTankAsync();
        foreach (var tank in new[] { onlyMeTank, privateStreamTank, mixedTank }) await FollowAsync(viewer, tank);
        await AddCareEntryAsync(onlyMeTank, onlyMeOwner.ProfileId, Ago(1), onlyMe: true);
        await AddCareEntryAsync(privateStreamTank, streamOwner.ProfileId, Ago(1));
        await AddCareEntryAsync(mixedTank, mixedOwner.ProfileId, Ago(5), kind: CareKind.WaterChange);
        await AddCareEntryAsync(mixedTank, mixedOwner.ProfileId, Ago(1), onlyMe: true);

        var body = await TodayAsync(viewer);

        Assert.Equal(new[] { mixedTank }, Ids(body));
        var update = body.GetProperty("items")[0].GetProperty("update");
        Assert.Equal("water_change", update.GetProperty("careKind").GetString());
        Assert.True((DateTimeOffset.UtcNow - update.GetProperty("at").GetDateTimeOffset()).TotalDays > 4);
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenAStoryOfATankWithAPrivateStream_WhenTodayLoads_ThenTheStoryStillShows()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, tank) = await PublicTankAsync(stream: TankVisibility.Private);
        await FollowAsync(viewer, tank);
        await AddStoryAsync(owner.ProfileId, tank, Ago(2));
        await AddCareEntryAsync(tank, owner.ProfileId, Ago(1));

        var update = Assert.Single((await TodayAsync(viewer)).GetProperty("items").EnumerateArray()).GetProperty("update");

        Assert.Equal("story", update.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GivenTanksTheMemberNoLongerReaches_WhenTodayLoads_ThenTheyAreLeftOutWithTheirCount()
    {
        var viewer = await NewMemberAsync(fixture);
        var (visibleOwner, visible) = await PublicTankAsync();
        var (narrowedOwner, narrowed) = await PublicTankAsync();
        var (selectedOwner, selectedOnly) = await PublicTankAsync(profile: TankVisibility.Selected, stream: TankVisibility.Selected);
        var (followersOwner, followersOnly) = await PublicTankAsync(profile: TankVisibility.Followers, stream: TankVisibility.Followers);
        foreach (var tank in new[] { visible, narrowed, selectedOnly, followersOnly }) await FollowAsync(viewer, tank);
        foreach (var (owner, tank) in new[] { (visibleOwner, visible), (narrowedOwner, narrowed), (selectedOwner, selectedOnly), (followersOwner, followersOnly) })
            await AddStoryAsync(owner.ProfileId, tank, Ago(1));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == narrowed)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private));
        }

        var body = await TodayAsync(viewer);

        Assert.Equal(new long[] { followersOnly, visible }.OrderBy(i => i).ToArray(), Ids(body).OrderBy(i => i).ToArray());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenASelectedMemberWhoFollows_WhenTodayLoads_ThenTheTankOfTheSelectedLevelShows()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, tank) = await PublicTankAsync(profile: TankVisibility.Selected, stream: TankVisibility.Selected);
        await FollowAsync(viewer, tank);
        await AddStoryAsync(owner.ProfileId, tank, Ago(1));
        Assert.Empty(Ids(await TodayAsync(viewer)));
        await using (var db = fixture.CreateDbContext())
        {
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank, MemberProfileId = viewer.ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(new[] { tank }, Ids(await TodayAsync(viewer)));
    }

    [Fact]
    public async Task GivenPendingFollowsAndOwnTanks_WhenTodayLoads_ThenOnlyApprovedFollowsOfOtherMembersShow()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, pending) = await PublicTankAsync();
        await FollowAsync(viewer, pending, FollowStatus.Pending);
        await AddStoryAsync(owner.ProfileId, pending, Ago(1));
        var own = await NewTankAsync(fixture, viewer.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await FollowAsync(viewer, own);
        await AddStoryAsync(viewer.ProfileId, own, Ago(1));

        Assert.Empty(Ids(await TodayAsync(viewer)));
    }

    [Fact]
    public async Task GivenASuspendedOwner_WhenTodayLoads_ThenTheTankIsLeftOut()
    {
        var viewer = await NewMemberAsync(fixture);
        var (suspendedOwner, suspendedTank) = await PublicTankAsync();
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == suspendedOwner.ProfileId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SuspendedUntil, DateTimeOffset.UtcNow.AddDays(3)));
        }

        await FollowAsync(viewer, suspendedTank);
        await AddStoryAsync(suspendedOwner.ProfileId, suspendedTank, Ago(1));
        await AddCareEntryAsync(suspendedTank, suspendedOwner.ProfileId, Ago(1));

        var body = await TodayAsync(viewer);

        Assert.Empty(Ids(body));
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task GivenDraftAndDeletedStories_WhenTodayLoads_ThenTheyAreNoUpdates()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, tank) = await PublicTankAsync();
        await FollowAsync(viewer, tank);
        await AddStoryAsync(owner.ProfileId, tank, Ago(1), StoryState.Draft);
        var deleted = await AddStoryAsync(owner.ProfileId, tank, Ago(1));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Stories.Where(s => s.Id == deleted).ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTimeOffset.UtcNow));
        }

        Assert.Empty(Ids(await TodayAsync(viewer)));
    }

    [Fact]
    public async Task GivenAPhotoOfADeletedTank_WhenTodayLoads_ThenItIsLeftOut()
    {
        var viewer = await NewMemberAsync(fixture);
        var (owner, tank) = await PublicTankAsync();
        await FollowAsync(viewer, tank);
        await AddPhotoAsync(tank, owner.ProfileId, MediaLinkRole.TimeLapse, Ago(1));
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
        }

        Assert.Empty(Ids(await TodayAsync(viewer)));
    }

    [Fact]
    public async Task GivenNoLogin_WhenCallingTheFollowedRoutes_ThenTheRequestIsRefused()
    {
        using var today = await SendAsync(fixture, HttpMethod.Get, "/api/me/today/followed");
        Assert.Equal(HttpStatusCode.Unauthorized, today.StatusCode);
        using var list = await SendAsync(fixture, HttpMethod.Get, "/api/me/followed-tanks");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task GivenManyFollowedTanks_WhenListingAllOfThem_ThenTheyArePagedAndTanksWithoutUpdatesComeLast()
    {
        var viewer = await NewMemberAsync(fixture);
        var (oldOwner, old) = await PublicTankAsync();
        var (freshOwner, fresh) = await PublicTankAsync();
        var (_, quiet) = await PublicTankAsync();
        var (hiddenOwner, hidden) = await PublicTankAsync(profile: TankVisibility.Private, stream: TankVisibility.Private);
        foreach (var tank in new[] { old, fresh, quiet, hidden }) await FollowAsync(viewer, tank);
        await AddStoryAsync(oldOwner.ProfileId, old, Ago(90));
        await AddStoryAsync(freshOwner.ProfileId, fresh, Ago(1));
        await AddStoryAsync(hiddenOwner.ProfileId, hidden, Ago(1));

        using var response = await SendAsync(fixture, HttpMethod.Get, "/api/me/followed-tanks?limit=2", viewer.Token);
        var first = await BodyAsync(response);
        using var secondResponse = await SendAsync(fixture, HttpMethod.Get, "/api/me/followed-tanks?limit=2&offset=2", viewer.Token);
        var second = await BodyAsync(secondResponse);

        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(new[] { fresh, old }, Ids(first));
        Assert.Equal(new[] { quiet }, Ids(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("items")[0].GetProperty("update").ValueKind);
    }
}
