using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Decommissioning.DecommissionTestSupport;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Decommissioning;

/// <summary>
/// A decommissioned tank has no due care of the paused kinds, no reminders and a heatmap that marks the
/// time; after the tank is put back into operation the plan runs from that day.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-6")]
public sealed class DecommissionedCareTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 15);
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.TankDecommissionings.Where(d => _tanks.Contains(d.TankId)).ExecuteDeleteAsync();
        await db.CarePlanRuleChanges.Where(r => _tanks.Contains(r.TankId)).ExecuteDeleteAsync();
        await db.CarePlanRules.Where(r => _tanks.Contains(r.TankId)).ExecuteDeleteAsync();
        if (_tanks.Count > 0) await RemoveTanksAsync(fixture, _tanks.ToArray());
    }

    private async Task<(Member Owner, long Tank)> TankWithHistoryAsync(TankVisibility profile = TankVisibility.Public, TankVisibility stream = TankVisibility.Public)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, profile, stream);
        _tanks.Add(tank);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        using var rule = await SendAsync(fixture, HttpMethod.Patch, $"/api/tanks/{tank}/care/plan/measurement?date={Today}", owner.Token, new { rhythmDays = 3 });
        Assert.True(rule.IsSuccessStatusCode);
        await using var db = fixture.CreateDbContext();
        db.CareEntries.Add(new Cichlids.Domain.Entities.CareEntry
        {
            TankId = tank, AuthorProfileId = owner.ProfileId, Kind = CareKind.Measurement, OccurredAt = At(10, 12), RecordedAt = At(10, 12),
        });
        await db.SaveChangesAsync();
        return (owner, tank);
    }

    private async Task<List<(long Tank, string Kind, JsonElement Item)>> DueAsync(string token, string date = Today)
    {
        using var due = await SendAsync(fixture, HttpMethod.Get, $"/api/me/care/due?date={date}", token);
        return (await BodyAsync(due)).GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("tankId").GetInt64(), i.GetProperty("kind").GetString()!, i.Clone())).ToList();
    }

    [Fact]
    public async Task GivenAllCarePaused_WhenReadingTheDueCareAndThePlan_ThenTheTankHasNoDueCare()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        Assert.Equal(2, (await DueAsync(owner.Token)).Count(i => i.Tank == tank));

        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 13), null);

        Assert.DoesNotContain(await DueAsync(owner.Token), i => i.Tank == tank);
        using var plan = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/care/plan?date={Today}", owner.Token);
        var rules = (await BodyAsync(plan)).GetProperty("rules").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!);
        Assert.All(rules.Values.Where(r => r.GetProperty("rhythmDays").ValueKind != JsonValueKind.Null), r =>
        {
            Assert.True(r.GetProperty("paused").GetBoolean());
            Assert.Equal(JsonValueKind.Null, r.GetProperty("due").ValueKind);
        });
    }

    [Fact]
    public async Task GivenOnlyTheWaterChangePaused_WhenReadingTheDueCare_ThenMeasurementsStayDue()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.WaterChange, new DateOnly(2026, 10, 13), null);

        var items = (await DueAsync(owner.Token)).Where(i => i.Tank == tank).ToList();
        Assert.Equal(["measurement"], items.Select(i => i.Kind));
        Assert.Equal("due_today", items[0].Item.GetProperty("due").GetProperty("state").GetString());
    }

    [Fact]
    public async Task GivenAPastDecommissioning_WhenReadingTheDueCare_ThenThePlanRunsFromTheDayTheTankWasPutBackIntoOperation()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 14));

        var water = (await DueAsync(owner.Token)).Single(i => i.Tank == tank && i.Kind == "water_change").Item;
        Assert.Equal("2026-10-21", water.GetProperty("due").GetProperty("dueDate").GetString());
        Assert.Equal("due_in", water.GetProperty("due").GetProperty("state").GetString());
        var window = water.GetProperty("streak");
        Assert.Equal("2026-10-21", window.GetProperty("reminderFrom").GetString());
        Assert.Equal("2026-10-24", window.GetProperty("deadline").GetString());
        var measurement = (await DueAsync(owner.Token)).Single(i => i.Tank == tank && i.Kind == "measurement").Item;
        Assert.Equal("2026-10-17", measurement.GetProperty("due").GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task GivenADecommissioningFromTheLocalDateOfTheMember_WhenReadingAtTheDayBoundary_ThenThePauseFollowsTheDateOfTheReader()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 17), null);

        Assert.Contains(await DueAsync(owner.Token, "2026-10-16"), i => i.Tank == tank);
        Assert.DoesNotContain(await DueAsync(owner.Token, "2026-10-17"), i => i.Tank == tank);
        Assert.DoesNotContain(await DueAsync(owner.Token, "2026-10-18"), i => i.Tank == tank);
    }

    [Fact]
    public async Task GivenAWaterChangeOnlyDecommissioning_WhenItEnds_ThenOnlyTheWaterChangePlanRestarts()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.WaterChange, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 14));

        var items = await DueAsync(owner.Token);
        Assert.Equal("2026-10-21", items.Single(i => i.Tank == tank && i.Kind == "water_change").Item.GetProperty("due").GetProperty("dueDate").GetString());
        Assert.Equal("2026-10-15", items.Single(i => i.Tank == tank && i.Kind == "measurement").Item.GetProperty("due").GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task GivenTheStreakOfADecommissionedTank_WhenReadingIt_ThenItIsPausedForTheOwnerAndContinuesAfterwards()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        var url = $"/api/tanks/{tank}/streaks?date={Today}&utcOffsetMinutes=0";
        var running = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, url, owner.Token))).GetProperty("waterChange");
        Assert.Equal("running", running.GetProperty("state").GetString());
        Assert.Equal("2026-10-18", running.GetProperty("deadline").GetString());

        var id = await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.WaterChange, new DateOnly(2026, 10, 10), null);
        var paused = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, url, owner.Token))).GetProperty("waterChange");
        Assert.Equal("paused", paused.GetProperty("state").GetString());
        Assert.Equal(1, paused.GetProperty("current").GetInt32());

        await using (var db = fixture.CreateDbContext())
        {
            await db.TankDecommissionings.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.EndedAt, At(new DateOnly(2026, 10, 14))).SetProperty(d => d.EndDate, new DateOnly(2026, 10, 14))
                .SetProperty(d => d.EndMediaItemId, d => d.StartMediaItemId));
        }
        var after = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, url, owner.Token))).GetProperty("waterChange");
        Assert.Equal("running", after.GetProperty("state").GetString());
        Assert.Equal("2026-10-24", after.GetProperty("deadline").GetString());
        Assert.Equal(1, after.GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task GivenADecommissionedTank_WhenReadingTheReminderWindows_ThenNoReminderFalls()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 10), null);
        foreach (var date in new[] { "2026-10-15", "2026-10-17", "2026-10-18" })
            Assert.DoesNotContain(await DueAsync(owner.Token, date), i => i.Tank == tank);
    }

    [Fact]
    public async Task GivenAllCarePaused_WhenReadingTheHeatmap_ThenTheDecommissionedDaysAreMarked()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13));
        var url = $"/api/tanks/{tank}/care/heatmap?date={Today}&utcOffsetMinutes=0";

        var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, url, owner.Token));
        Assert.Equal(["2026-10-10", "2026-10-11", "2026-10-12"], body.GetProperty("decommissionedDays").EnumerateArray().Select(d => d.GetString()));
        var visitor = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, url));
        Assert.Equal(3, visitor.GetProperty("decommissionedDays").GetArrayLength());
    }

    [Fact]
    public async Task GivenOnlyTheWaterChangePaused_WhenReadingTheHeatmap_ThenNoDayIsMarked()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.WaterChange, new DateOnly(2026, 10, 10), null);
        var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/care/heatmap?date={Today}&utcOffsetMinutes=0", owner.Token));
        Assert.Equal(0, body.GetProperty("decommissionedDays").GetArrayLength());
    }

    [Fact]
    public async Task GivenEveryTankOfTheMemberDecommissioned_WhenReadingTheOwnHeatmap_ThenOnlyTheDaysAllTanksRestAreMarked()
    {
        var (owner, tank) = await TankWithHistoryAsync();
        var other = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        _tanks.Add(other);
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 10), null);
        await AddAsync(fixture, other, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14));

        var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/me/care/heatmap?date={Today}&utcOffsetMinutes=0", owner.Token));
        Assert.Equal(["2026-10-12", "2026-10-13"], body.GetProperty("decommissionedDays").EnumerateArray().Select(d => d.GetString()));
    }

    [Fact]
    public async Task GivenTheTankList_WhenTheTankIsDecommissioned_ThenOnlyViewersOfTheStreamSeeTheMark()
    {
        var (owner, tank) = await TankWithHistoryAsync(stream: TankVisibility.Private);
        await AddAsync(fixture, tank, owner.ProfileId, DecommissionScope.AllCare, new DateOnly(2026, 10, 10), null);
        async Task<bool?> MarkAsync(string? token)
        {
            var list = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/tanks?userId={owner.ProfileId}", token));
            return list.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("id").GetInt64() == tank)
                .Select(i => (bool?)i.GetProperty("decommissioned").GetBoolean()).SingleOrDefault();
        }
        Assert.True(await MarkAsync(owner.Token));
        Assert.False(await MarkAsync(null));
        Assert.False(await MarkAsync((await NewMemberAsync(fixture)).Token));
    }
}
