using System.Net;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Decommissioning.DecommissionTestSupport;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// The milestones on Today: a kept streak, a full tank year and the first met water change plan show
/// for a week from the day they are reached, until the owner dismisses them.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.24")]
public sealed class TodayMilestoneTests(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => RemoveEventsAsync(fixture, "streak_freeze");

    private static string ListUrl(string date = Today) => $"/api/me/today/milestones?date={date}&utcOffsetMinutes=0";

    private static string DismissUrl(string key) => $"/api/me/today/milestones/{key}/dismissal";

    private async Task<JsonElement[]> ListAsync(Member member, string date = Today)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, ListUrl(date), member.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("items").EnumerateArray().ToArray();
    }

    private async Task<(Member Owner, long Tank)> OwnerAsync()
    {
        var owner = await NewMemberAsync(fixture);
        return (owner, await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private));
    }

    // Four weeks of photos that end in the week of Today (Thursday 2026-10-15).
    private async Task AddFourWeeksAsync(long tank, long profile)
    {
        foreach (var (month, day) in new[] { (9, 22), (9, 29), (10, 6), (10, 13) })
            await AddSeriesPhotoAsync(fixture, tank, profile, At(month, day));
    }

    private async Task SetCreatedAsync(long tank, DateTimeOffset createdAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, createdAt));
    }

    private static string KeptStreakKey(long tank, int weeks) => $"kept_streak:{tank}:{weeks}";

    [Fact]
    public async Task GivenAStreakOfFourWeeks_WhenTheOwnerOpensTodayWithinSevenDays_ThenTheCongratulationShowsUntilItIsDismissed()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(9, 1));
        await AddFourWeeksAsync(tank, owner.ProfileId);

        var shown = Assert.Single(await ListAsync(owner));
        Assert.Equal(KeptStreakKey(tank, 4), shown.GetProperty("key").GetString());
        Assert.Equal("kept_streak", shown.GetProperty("kind").GetString());
        Assert.Equal(tank, shown.GetProperty("tankId").GetInt64());
        Assert.Equal(4, shown.GetProperty("value").GetInt32());
        Assert.Equal("2026-10-13", shown.GetProperty("reachedOn").GetString());
        Assert.Single(await ListAsync(owner, "2026-10-19"));
        Assert.Empty(await ListAsync(owner, "2026-10-20"));

        using var dismissed = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(tank, 4)), owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        Assert.Empty(await ListAsync(owner));
        Assert.Empty(await ListAsync(owner, "2026-10-17"));
    }

    [Fact]
    public async Task GivenADismissedMilestone_WhenDismissingItAgain_ThenOneDismissalIsStored()
    {
        var (owner, tank) = await OwnerAsync();
        using var first = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(tank, 12)), owner.Token);
        using var second = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(tank, 12)), owner.Token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await using var db = fixture.CreateDbContext();
        var row = await db.MilestoneDismissals.SingleAsync(d => d.ProfileId == owner.ProfileId);
        Assert.Equal(KeptStreakKey(tank, 12), row.Key);
    }

    [Fact]
    public async Task GivenAStreakReachedMoreThanSevenDaysAgo_WhenTheOwnerOpensToday_ThenNothingShows()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(9, 1));
        foreach (var (month, day) in new[] { (9, 8), (9, 15), (9, 22), (9, 29) }) await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(month, day));

        Assert.Empty(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenThreeWeeks_WhenTheOwnerOpensToday_ThenNoStreakMilestoneShows()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(9, 1));
        foreach (var (month, day) in new[] { (9, 29), (10, 6), (10, 13) }) await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(month, day));

        Assert.Empty(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenAFullTankYear_WhenTheOwnerOpensToday_ThenTheAnniversaryShowsWithItsYear()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(10, 12).AddYears(-2));

        var shown = Assert.Single(await ListAsync(owner));

        Assert.Equal($"tank_anniversary:{tank}:2", shown.GetProperty("key").GetString());
        Assert.Equal("tank_anniversary", shown.GetProperty("kind").GetString());
        Assert.Equal(2, shown.GetProperty("value").GetInt32());
        Assert.Equal("2026-10-12", shown.GetProperty("reachedOn").GetString());
    }

    [Fact]
    public async Task GivenDecommissionedDays_WhenTheCalendarAnniversaryPasses_ThenTheTankYearIsNotCompleteYet()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(10, 12).AddYears(-1));
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 11));

        Assert.Empty(await ListAsync(owner));
        var shown = Assert.Single(await ListAsync(owner, "2026-10-22"));
        Assert.Equal("2026-10-22", shown.GetProperty("reachedOn").GetString());
    }

    [Fact]
    public async Task GivenStreaksSwitchedOff_WhenTheOwnerOpensToday_ThenOnlyTheStreakMilestonesAreLeftOut()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(10, 12).AddYears(-1));
        await AddFourWeeksAsync(tank, owner.ProfileId);
        Assert.Equal(2, (await ListAsync(owner)).Length);

        using var off = await SendAsync(fixture, HttpMethod.Put, "/api/me/settings", owner.Token, new { streaksEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var shown = Assert.Single(await ListAsync(owner));
        Assert.Equal("tank_anniversary", shown.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GivenTheFirstMetWaterChangePlan_WhenTheOwnerOpensToday_ThenTheFirstCareWeekShows()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(9, 1));
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 9), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 15), true);

        var shown = Assert.Single(await ListAsync(owner));

        Assert.Equal($"first_care_week:{tank}", shown.GetProperty("key").GetString());
        Assert.Equal("2026-10-09", shown.GetProperty("reachedOn").GetString());
    }

    [Fact]
    public async Task GivenAMissedPeriod_WhenTheOwnerOpensToday_ThenNoFirstCareWeekShows()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(9, 1));
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(9, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);

        Assert.Empty(await ListAsync(owner));
    }

    [Fact]
    public async Task GivenMilestonesOfAnotherMember_WhenOpeningToday_ThenOnlyOwnMilestonesShow()
    {
        var (owner, tank) = await OwnerAsync();
        var other = await NewMemberAsync(fixture);
        await SetCreatedAsync(tank, At(10, 12).AddYears(-1));

        Assert.Single(await ListAsync(owner));
        Assert.Empty(await ListAsync(other));
    }

    [Fact]
    public async Task GivenKeysThatAreNotMilestonesOfAnOwnTank_WhenDismissing_ThenTheRequestIsRefused()
    {
        var (owner, tank) = await OwnerAsync();
        var (_, foreign) = await OwnerAsync();

        foreach (var key in new[] { "nonsense", $"kept_streak:{tank}:5", $"tank_anniversary:{tank}:0", $"first_care_week:{tank}:1" })
        {
            using var invalid = await SendAsync(fixture, HttpMethod.Put, DismissUrl(key), owner.Token);
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "milestone_key_invalid");
        }

        using var foreignTank = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(foreign, 4)), owner.Token);
        await AssertProblemAsync(foreignTank, HttpStatusCode.NotFound, "milestone_not_found");
        using var unknownTank = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(long.MaxValue, 4)), owner.Token);
        await AssertProblemAsync(unknownTank, HttpStatusCode.NotFound, "milestone_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MilestoneDismissals.AnyAsync(d => d.ProfileId == owner.ProfileId));
    }

    [Fact]
    public async Task GivenKeysThatDifferFromTheirCanonicalForm_WhenDismissing_ThenTheRequestIsRefusedAndNothingIsStored()
    {
        var (owner, tank) = await OwnerAsync();
        var zeros = new string('0', 60);

        foreach (var key in new[] { $"first_care_week:{new string('0', 3)}{tank}", $"kept_streak:{tank}:04", $"kept_streak:+{tank}:4", $"first_care_week:{zeros}{tank}", $"tank_anniversary:{tank}:{zeros}1", $"kept_streak:{tank}:4 " })
        {
            using var response = await SendAsync(fixture, HttpMethod.Put, DismissUrl(key), owner.Token);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "milestone_key_invalid");
        }

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.MilestoneDismissals.AnyAsync(d => d.ProfileId == owner.ProfileId));
    }

    [Fact]
    public async Task GivenAnAnniversaryTheTankHasNotReached_WhenDismissing_ThenTheRequestIsRefused()
    {
        var (owner, tank) = await OwnerAsync();
        await SetCreatedAsync(tank, At(10, 12).AddYears(-2));

        using var reached = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"tank_anniversary:{tank}:2"), owner.Token);
        using var earlier = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"tank_anniversary:{tank}:1"), owner.Token);
        using var beyond = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"tank_anniversary:{tank}:3"), owner.Token);
        using var huge = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"tank_anniversary:{tank}:2147483647"), owner.Token);

        Assert.Equal(HttpStatusCode.NoContent, reached.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, earlier.StatusCode);
        await AssertProblemAsync(beyond, HttpStatusCode.BadRequest, "milestone_key_invalid");
        await AssertProblemAsync(huge, HttpStatusCode.BadRequest, "milestone_key_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.MilestoneDismissals.CountAsync(d => d.ProfileId == owner.ProfileId));
    }

    [Fact]
    public async Task GivenTheFirstCareWeekKey_WhenDismissingIt_ThenTheStoredKeyIsTheCanonicalOne()
    {
        var (owner, tank) = await OwnerAsync();
        using var response = await SendAsync(fixture, HttpMethod.Put, DismissUrl($"first_care_week:{tank}"), owner.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal($"first_care_week:{tank}", (await db.MilestoneDismissals.SingleAsync(d => d.ProfileId == owner.ProfileId)).Key);
    }

    [Fact]
    public async Task GivenNoLoginOrABadDate_WhenCallingTheMilestoneRoutes_ThenTheRequestIsRefused()
    {
        var (owner, tank) = await OwnerAsync();
        using var anonymous = await SendAsync(fixture, HttpMethod.Get, ListUrl());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var anonymousDismiss = await SendAsync(fixture, HttpMethod.Put, DismissUrl(KeptStreakKey(tank, 4)));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDismiss.StatusCode);
        using var badDate = await SendAsync(fixture, HttpMethod.Get, "/api/me/today/milestones?date=yesterday", owner.Token);
        await AssertProblemAsync(badDate, HttpStatusCode.BadRequest, "today_invalid_date");
    }
}
