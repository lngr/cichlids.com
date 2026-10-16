using System.Net;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Streaks;

[Collection(ApiCollection.Name)]
public sealed class StreakEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static string Url(long tank, string date = Today, int offset = 0) =>
        $"/api/tanks/{tank}/streaks?date={date}&utcOffsetMinutes={offset}";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => RemoveEventsAsync(fixture, "streak_freeze", "vacation");

    private async Task<(Member Owner, long Tank)> OwnerWithHistoryAsync(TankVisibility profile = TankVisibility.Public,
        TankVisibility stream = TankVisibility.Public)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, profile, stream);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);
        return (owner, tank);
    }

    [Fact]
    public async Task GivenAVerifiedHistory_WhenTheOwnerReadsTheStreaks_ThenTheWaterChangeStreakShowsPeriodsDeadlineAndFreezes()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        using var response = await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var water = (await BodyAsync(response)).GetProperty("waterChange");
        Assert.Equal("running", water.GetProperty("state").GetString());
        Assert.Equal(2, water.GetProperty("current").GetInt32());
        Assert.Equal(2, water.GetProperty("currentVerified").GetInt32());
        Assert.Equal(2, water.GetProperty("longest").GetInt32());
        Assert.Equal("2026-10-14", water.GetProperty("lastWaterChange").GetString());
        Assert.Equal("2026-10-24", water.GetProperty("deadline").GetString());
        Assert.Equal(2, water.GetProperty("freezesLeft").GetInt32());
        Assert.Equal(2, water.GetProperty("freezesPerYear").GetInt32());
    }

    [Fact]
    public async Task GivenAnUnverifiedWaterChange_WhenTheOwnerReadsTwice_ThenOneFreezeIsRecordedWithOneEvent()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        var unverified = await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), false);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);

        var firstRead = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token))).GetProperty("waterChange");
        var secondRead = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token))).GetProperty("waterChange");
        Assert.Equal(2, firstRead.GetProperty("current").GetInt32());
        Assert.Equal(1, firstRead.GetProperty("currentVerified").GetInt32());
        Assert.Equal(1, firstRead.GetProperty("freezesLeft").GetInt32());
        Assert.Equal(firstRead.GetRawText(), secondRead.GetRawText());

        await using var db = fixture.CreateDbContext();
        var freeze = await db.StreakFreezes.SingleAsync(f => f.TankId == tank);
        Assert.Equal(unverified, freeze.CareEntryId);
        Assert.Equal(1, freeze.TankYear);
        Assert.Equal(new DateOnly(2026, 10, 1), freeze.PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 8), freeze.PeriodEnd);
        var events = (await db.OutboxEvents.Where(e => e.EventType == "streak.freeze_used").ToListAsync())
            .Where(e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("tankId").GetInt64() == tank).ToList();
        var used = Assert.Single(events);
        using var payload = JsonDocument.Parse(used.Payload);
        Assert.Equal(unverified, payload.RootElement.GetProperty("careEntryId").GetInt64());
        Assert.Equal(owner.ProfileId, payload.RootElement.GetProperty("profileId").GetInt64());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(120, 1)]
    public async Task GivenATankCreatedLateOnAnAnniversaryEve_WhenTheOwnerReads_ThenTheTankYearFollowsTheLocalCreationDate(int offset, int tankYear)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await using (var db = fixture.CreateDbContext())
            await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, new DateTimeOffset(2025, 10, 10, 23, 30, 0, TimeSpan.Zero)));
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 3), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 10), false);

        using var response = await SendAsync(fixture, HttpMethod.Get, Url(tank, offset: offset), owner.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(tankYear, (await verify.StreakFreezes.SingleAsync(f => f.TankId == tank)).TankYear);
    }

    [Fact]
    public async Task GivenAnUnverifiedWaterChange_WhenAnotherMemberReads_ThenNoFreezeIsRecorded()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), false);
        using var response = await SendAsync(fixture, HttpMethod.Get, Url(tank), viewer.Token);
        Assert.Equal(1, (await BodyAsync(response)).GetProperty("waterChange").GetProperty("current").GetInt32());
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.StreakFreezes.AnyAsync(f => f.TankId == tank));
    }

    [Fact]
    public async Task GivenNoFreezeLeft_WhenTheOwnerReads_ThenTheUnverifiedPeriodBreaksTheStreak()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(9, 1), true);
        foreach (var day in new[] { 8, 15, 22 }) await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(9, day), false);
        var water = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank, "2026-09-23"), owner.Token))).GetProperty("waterChange");
        Assert.Equal(0, water.GetProperty("current").GetInt32());
        Assert.Equal(2, water.GetProperty("longest").GetInt32());
        Assert.Equal(0, water.GetProperty("freezesLeft").GetInt32());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.StreakFreezes.CountAsync(f => f.TankId == tank));
    }

    [Fact]
    public async Task GivenAPublicTank_WhenAnotherMemberOrAnAnonymousVisitorReads_ThenTheyGetBothStreaksWithoutOwnerDetails()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 6));
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 13));
        var viewer = await NewMemberAsync(fixture);
        foreach (var token in new string?[] { viewer.Token, null })
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, Url(tank), token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await BodyAsync(response);
            var water = body.GetProperty("waterChange");
            Assert.Equal(2, water.GetProperty("current").GetInt32());
            Assert.Equal(JsonValueKind.Null, water.GetProperty("deadline").ValueKind);
            Assert.Equal(JsonValueKind.Null, water.GetProperty("freezesLeft").ValueKind);
            var timeLapse = body.GetProperty("timeLapse");
            Assert.Equal(2, timeLapse.GetProperty("current").GetInt32());
            Assert.Equal(JsonValueKind.Null, timeLapse.GetProperty("keepingPhotoIds").ValueKind);
            Assert.Equal(JsonValueKind.Null, timeLapse.GetProperty("paused").ValueKind);
        }
    }

    [Fact]
    public async Task GivenAPublicProfileWithAPrivateStream_WhenAnotherMemberReads_ThenOnlyTheTimeLapseStreakIsShown()
    {
        var (owner, tank) = await OwnerWithHistoryAsync(TankVisibility.Public, TankVisibility.Private);
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 13));
        var viewer = await NewMemberAsync(fixture);
        var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), viewer.Token));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("waterChange").ValueKind);
        Assert.Equal(1, body.GetProperty("timeLapse").GetProperty("current").GetInt32());
        var ownBody = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token));
        Assert.Equal(2, ownBody.GetProperty("waterChange").GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenAnotherMemberOrAnAnonymousVisitorReads_ThenTheTankIsAnsweredAsMissing()
    {
        var (owner, tank) = await OwnerWithHistoryAsync(TankVisibility.Private, TankVisibility.Private);
        var viewer = await NewMemberAsync(fixture);
        foreach (var token in new string?[] { viewer.Token, null })
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, Url(tank), token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "streak_not_found");
        }
        using var own = await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var missing = await SendAsync(fixture, HttpMethod.Get, Url(long.MaxValue), owner.Token);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "streak_not_found");
    }

    [Fact]
    public async Task GivenOnlyMeEntries_WhenAnotherMemberReads_ThenTheStreakStillCountsThem()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        var hidden = await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        await using (var db = fixture.CreateDbContext())
            await db.CareEntries.Where(e => e.Id == hidden).ExecuteUpdateAsync(s => s.SetProperty(e => e.OnlyMe, true));
        var seen = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), viewer.Token));
        Assert.Equal(1, seen.GetProperty("waterChange").GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenOthersRead_ThenItIsNotExposedAndTheOwnerSeesThePause()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 13));
        await AddVacationAsync(fixture, owner.ProfileId, new DateOnly(2026, 10, 15), null);
        var viewer = await NewMemberAsync(fixture);

        var own = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token));
        Assert.Equal("paused", own.GetProperty("waterChange").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("waterChange").GetProperty("deadline").ValueKind);
        Assert.True(own.GetProperty("timeLapse").GetProperty("paused").GetBoolean());
        var ownVacation = Assert.Single(own.GetProperty("vacations").EnumerateArray());
        Assert.True(ownVacation.GetProperty("active").GetBoolean());

        var seen = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), viewer.Token));
        Assert.Equal("running", seen.GetProperty("waterChange").GetProperty("state").GetString());
        Assert.Empty(seen.GetProperty("vacations").EnumerateArray());
        Assert.DoesNotContain("2026-10-15", seen.GetRawText().Replace("\"lastWaterChange\":\"2026-10-14\"", ""));
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("timeLapse").GetProperty("paused").ValueKind);
    }

    [Fact]
    public async Task GivenAPastVacation_WhenOthersRead_ThenItIsListedAndThePausedDaysDoNotBreakTheStreak()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(9, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(9, 8), true);
        await AddVacationAsync(fixture, owner.ProfileId, new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 9));
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 12), true);
        var viewer = await NewMemberAsync(fixture);
        var seen = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), viewer.Token));
        Assert.Equal("running", seen.GetProperty("waterChange").GetProperty("state").GetString());
        Assert.Equal(1, seen.GetProperty("waterChange").GetProperty("current").GetInt32());
        var vacation = Assert.Single(seen.GetProperty("vacations").EnumerateArray());
        Assert.Equal("2026-09-10", vacation.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-09", vacation.GetProperty("endDate").GetString());
        Assert.False(vacation.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task GivenPhotosFromTheCameraAndTheLibrary_WhenReadingTheTimeLapseStreak_ThenOnlyCameraPhotosCount()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var sole = await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(9, 30));
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 6));
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 7));
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 13), capturedInApp: false);
        var series = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank), owner.Token))).GetProperty("timeLapse");
        Assert.Equal(2, series.GetProperty("current").GetInt32());
        Assert.False(series.GetProperty("thisWeekHasPhoto").GetBoolean());
        Assert.True(series.GetProperty("thisWeekOpen").GetBoolean());
        Assert.Equal("2026-10-12", series.GetProperty("weekStart").GetString());
        Assert.Equal("2026-10-18", series.GetProperty("weekEnd").GetString());
        Assert.Equal([sole], series.GetProperty("keepingPhotoIds").EnumerateArray().Select(i => i.GetInt64()));
    }

    [Fact]
    public async Task GivenTheMembersOffset_WhenReading_ThenPhotosFallIntoTheLocalWeek()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 11, 23));
        var utc = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank, "2026-10-15", 0), owner.Token))).GetProperty("timeLapse");
        Assert.False(utc.GetProperty("thisWeekHasPhoto").GetBoolean());
        var ahead = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, Url(tank, "2026-10-15", 120), owner.Token))).GetProperty("timeLapse");
        Assert.True(ahead.GetProperty("thisWeekHasPhoto").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("&date=2026-13-01")]
    [InlineData("&date=today")]
    public async Task GivenAnInvalidDate_WhenReading_ThenTheProblemHasACode(string suffix)
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/streaks?x=1{suffix}", owner.Token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "streak_date_invalid");
        using var offset = await SendAsync(fixture, HttpMethod.Get, Url(tank, offset: 9999), owner.Token);
        await AssertProblemAsync(offset, HttpStatusCode.BadRequest, "streak_date_invalid");
    }

    [Fact]
    public async Task GivenTheOverview_WhenTheOwnerReads_ThenEveryLiveTankHasItsStreaksAndTheFreezeAllotmentIsShown()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private, "Overview tank");
        var second = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public, "Second tank");
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);
        await AddVacationAsync(fixture, owner.ProfileId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5));
        var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/me/streaks?date={Today}", owner.Token));
        Assert.Equal(2, body.GetProperty("freezesPerYear").GetInt32());
        var tanks = body.GetProperty("tanks").EnumerateArray().ToDictionary(t => t.GetProperty("tankId").GetInt64());
        Assert.Equal([tank, second], tanks.Keys.Order());
        Assert.Equal("Overview tank", tanks[tank].GetProperty("title").GetString());
        Assert.Equal(1, tanks[tank].GetProperty("waterChange").GetProperty("current").GetInt32());
        Assert.Equal("no_history", tanks[second].GetProperty("waterChange").GetProperty("state").GetString());
        Assert.Single(body.GetProperty("vacations").EnumerateArray());
    }

    [Fact]
    public async Task GivenNoLogin_WhenReadingTheOverview_ThenTheProblemHasACode()
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, $"/api/me/streaks?date={Today}", null);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "streak_auth_required");
        using var bad = await SendAsync(fixture, HttpMethod.Get, "/api/me/streaks", (await NewMemberAsync(fixture)).Token);
        await AssertProblemAsync(bad, HttpStatusCode.BadRequest, "streak_date_invalid");
    }

    [Fact]
    public async Task GivenARhythmChange_WhenTheOwnerChangesTheRule_ThenTheHistoryOfTheRuleGrows()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        using var first = await SendAsync(fixture, HttpMethod.Patch, $"/api/tanks/{tank}/care/plan/water_change?date={Today}", owner.Token, new { rhythmDays = 14 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await SendAsync(fixture, HttpMethod.Patch, $"/api/tanks/{tank}/care/plan/water_change?date={Today}", owner.Token, new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var db = fixture.CreateDbContext();
        var history = await db.CarePlanRuleChanges.Where(c => c.TankId == tank).OrderBy(c => c.Id).ToListAsync();
        Assert.Equal([14, 14], history.Select(c => c.RhythmDays));
        Assert.Equal([true, false], history.Select(c => c.Enabled));
        Assert.All(history, c => Assert.Equal(CareKind.WaterChange, c.Kind));
    }
}
