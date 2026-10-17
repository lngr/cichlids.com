// Story: TASK-9.2
using System.Net;
using System.Text.Json;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Fish;

/// <summary>
/// The anniversary of a named fish on Today: it shows for a week from the day a full year since the
/// move-in is reached, until the owner dismisses it. Keys are canonical and name reached years only.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-9.2")]
public sealed class FishMilestoneTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly FishWorld _world = new(fixture);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _world.CleanAsync();

    private static string ListUrl(string date = FishWorld.Today) => $"/api/me/today/milestones?date={date}&utcOffsetMinutes=0";

    private static string DismissUrl(string key) => $"/api/me/today/milestones/{key}/dismissal";

    private async Task<JsonElement[]> ListAsync(Member member, string date = FishWorld.Today)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, ListUrl(date), member.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "fish_anniversary").ToArray();
    }

    private async Task<long> FishMovedInAsync(Member owner, long tank, string movedInOn)
    {
        var fish = await _world.AddFishAsync(owner, tank);
        await _world.SetMovedInAsync(fish, DateOnly.Parse(movedInOn));
        return fish;
    }

    [Fact]
    public async Task GivenAFullYearSinceTheMoveIn_WhenTheOwnerOpensTodayWithinSevenDays_ThenTheCongratulationShowsUntilItIsDismissed()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await FishMovedInAsync(owner, tank, "2025-10-12");

        var shown = Assert.Single(await ListAsync(owner));

        Assert.Equal($"fish_anniversary:{fish}:1", shown.GetProperty("key").GetString());
        Assert.Equal(1, shown.GetProperty("value").GetInt32());
        Assert.Equal("2026-10-12", shown.GetProperty("reachedOn").GetString());
        Assert.Equal(fish, shown.GetProperty("inhabitantId").GetInt64());
        Assert.Equal("Bruno", shown.GetProperty("inhabitantName").GetString());
        Assert.Equal(tank, shown.GetProperty("tankId").GetInt64());
        Assert.Single(await ListAsync(owner, "2026-10-18"));
        Assert.Empty(await ListAsync(owner, "2026-10-19"));
        Assert.Empty(await ListAsync(owner, "2026-10-11"));

        using var dismissed = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"fish_anniversary:{fish}:1"), owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        Assert.Empty(await ListAsync(owner));
        using var again = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"fish_anniversary:{fish}:1"), owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.MilestoneDismissals.CountAsync(d => d.ProfileId == owner.ProfileId));
    }

    [Fact]
    public async Task GivenNoFullYearOrNoMoveInDay_WhenTheOwnerOpensToday_ThenNothingShows()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        await FishMovedInAsync(owner, tank, "2025-10-20");
        var migrated = await _world.AddFishAsync(owner, tank, "Hugo");
        await _world.SetMovedInAsync(migrated, null);

        Assert.Empty(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenAGroupADeadFishAndAFishOfADeletedTank_WhenTheOwnerOpensToday_ThenNoneOfThemShows()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var gone = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        using (var group = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={FishWorld.Today}", owner.Token,
            new { speciesId = fixture.Seed.Tropheus.Id, count = 5, movedInOn = "2025-10-12" }))
            Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var dead = await FishMovedInAsync(owner, tank, "2025-10-12");
        await _world.PostAsync(dead, "/death", owner, new { on = "2026-05-01" });
        await FishMovedInAsync(owner, gone, "2025-10-12");
        await _world.SoftDeleteTankAsync(gone);

        Assert.Empty(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenAFishOfAnotherMember_WhenAStrangerOpensToday_ThenOnlyTheOwnerSeesTheCongratulation()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        await FishMovedInAsync(owner, tank, "2025-10-12");
        var stranger = await NewMemberAsync(fixture);

        Assert.Empty(await ListAsync(stranger));
        Assert.Single(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenLeapDayMoveIn_WhenTheYearIsNotALeapYear_ThenTheAnniversaryFallsOnTheLastDayOfFebruary()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await FishMovedInAsync(owner, tank, "2024-02-29");

        var shown = Assert.Single(await ListAsync(owner, "2025-03-01"));

        Assert.Equal($"fish_anniversary:{fish}:1", shown.GetProperty("key").GetString());
        Assert.Equal("2025-02-28", shown.GetProperty("reachedOn").GetString());
    }

    [Fact]
    public async Task GivenCanonicalKeysOfReachedYears_WhenDismissing_ThenTheyAreStoredOnceEach()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await FishMovedInAsync(owner, tank, "2024-10-12");

        foreach (var year in new[] { 1, 2 })
        {
            using var response = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"fish_anniversary:{fish}:{year}"), owner.Token);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.MilestoneDismissals.CountAsync(d => d.ProfileId == owner.ProfileId));
    }

    [Fact]
    public async Task GivenKeysThatAreNotCanonicalOrNotReached_WhenDismissing_ThenTheApiRefusesAndStoresNothing()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await FishMovedInAsync(owner, tank, "2025-10-12");
        var migrated = await _world.AddFishAsync(owner, tank, "Hugo");
        await _world.SetMovedInAsync(migrated, null);

        var invalid = new[]
        {
            $"fish_anniversary:{fish}:2", $"fish_anniversary:{fish}:0", $"fish_anniversary:{fish}:01", $"fish_anniversary:0{fish}:1",
            $"fish_anniversary:{fish}:101", $"fish_anniversary:{fish}:-1", $"fish_anniversary:{fish}", $"fish_anniversary:{fish}:1:1",
            $"fish_anniversary:{migrated}:1", $"fish_anniversary:+{fish}:1", $"fish_anniversary:{fish}: 1", $"Fish_Anniversary:{fish}:1",
        };
        foreach (var key in invalid)
        {
            using var response = await SendAsync(fixture, HttpMethod.Put, DismissUrl(Uri.EscapeDataString(key)), owner.Token);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{key}: {response.StatusCode}");
            Assert.Equal("milestone_key_invalid", (await BodyAsync(response)).GetProperty("code").GetString());
        }

        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.MilestoneDismissals.Where(d => d.ProfileId == owner.ProfileId).ToListAsync());
    }

    [Fact]
    public async Task GivenAFishOfAnotherMemberOrAGroupOrNoFish_WhenDismissing_ThenTheApiAnswersNotFound()
    {
        var (owner, tank) = await _world.OwnerWithTankAsync();
        var fish = await FishMovedInAsync(owner, tank, "2025-10-12");
        var stranger = await NewMemberAsync(fixture);
        using var group = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{tank}/inhabitants?date={FishWorld.Today}", owner.Token,
            new { speciesId = fixture.Seed.Tropheus.Id, count = 5, movedInOn = "2020-01-01" });
        var groupId = (await BodyAsync(group)).GetProperty("id").GetInt64();

        foreach (var (token, key) in new[] { (stranger.Token, $"fish_anniversary:{fish}:1"), (owner.Token, $"fish_anniversary:{groupId}:1"), (owner.Token, "fish_anniversary:999999999:1") })
        {
            using var response = await SendAsync(fixture, HttpMethod.Put, DismissUrl(key), token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "milestone_not_found");
        }
    }

    [Fact]
    public void TheKeyNamesTheFishAndTheYear()
    {
        Assert.Equal("fish_anniversary:7:3", Milestones.Key(MilestoneKind.FishAnniversary, 7, 3));
        var parsed = Milestones.TryParseKey("fish_anniversary:7:3");
        Assert.Equal((MilestoneKind.FishAnniversary, 7L, 3), parsed!.Value);
        Assert.Null(Milestones.TryParseKey("fish_anniversary:7:0"));
        Assert.Null(Milestones.TryParseKey("fish_anniversary:7:101"));
        Assert.Null(Milestones.TryParseKey("fish_anniversary:7"));
        Assert.Equal((MilestoneKind.FishAnniversary, 7L, 100), Milestones.TryParseKey("fish_anniversary:7:100")!.Value);
    }

    [Theory]
    [InlineData("2025-10-12", "2026-10-11", 0)]
    [InlineData("2025-10-12", "2026-10-12", 1)]
    [InlineData("2023-10-12", "2026-10-11", 2)]
    [InlineData("2024-02-29", "2025-02-27", 0)]
    [InlineData("2024-02-29", "2025-02-28", 1)]
    [InlineData("2024-02-29", "2028-02-28", 3)]
    [InlineData("2024-02-29", "2028-02-29", 4)]
    [InlineData("2026-10-12", "2026-10-11", 0)]
    public void TheCompletedYearsCountFromTheMoveInDay(string movedIn, string day, int years) =>
        Assert.Equal(years, Milestones.CompletedFishYears(DateOnly.Parse(movedIn), DateOnly.Parse(day)));

    [Fact]
    public void TheAnniversariesOfAWindowAreTheYearsCompletedWithinIt()
    {
        var movedIn = new DateOnly(2020, 12, 30);

        var years = Milestones.FishAnniversaries(movedIn, new DateOnly(2025, 12, 28), new DateOnly(2026, 1, 3));

        Assert.Equal([(5, new DateOnly(2025, 12, 30))], years);
        Assert.Empty(Milestones.FishAnniversaries(movedIn, new DateOnly(2020, 12, 30), new DateOnly(2021, 12, 29)));
        Assert.Single(Milestones.FishAnniversaries(movedIn, new DateOnly(2020, 12, 30), new DateOnly(2021, 12, 30)));
    }
}
