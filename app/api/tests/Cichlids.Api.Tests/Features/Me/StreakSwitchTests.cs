using System.Net;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

/// <summary>
/// A member turns the streaks off with one switch on the settings. With the switch off no streak, no
/// vacation share and no reminder window reaches the member or other viewers, while the evaluation and
/// the recorded freezes stay the ones the history yields.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.19")]
public sealed class StreakSwitchTests(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => RemoveEventsAsync(fixture, "streak_freeze", "vacation");

    private Task<HttpResponseMessage> SetAsync(Member member, bool enabled) =>
        SendAsync(fixture, HttpMethod.Put, "/api/me/settings", member.Token, new { streaksEnabled = enabled });

    private async Task<(Member Owner, long Tank)> OwnerWithHistoryAsync()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);
        await AddSeriesPhotoAsync(fixture, tank, owner.ProfileId, At(10, 13));
        return (owner, tank);
    }

    private async Task<JsonElement> TankStreaksAsync(long tank, string? token) =>
        await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/streaks?date={Today}&utcOffsetMinutes=0", token));

    private async Task<JsonElement> OwnStreaksAsync(Member member) =>
        await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/me/streaks?date={Today}&utcOffsetMinutes=0", member.Token));

    private async Task<JsonElement> VacationShareAsync(long profile, string token) =>
        (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{profile}", token))).GetProperty("vacationShare");

    [Fact]
    public async Task GivenANewMember_WhenReadingTheSettings_ThenStreaksAreOn()
    {
        var member = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Get, "/api/me/settings", member.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await BodyAsync(response)).GetProperty("streaksEnabled").GetBoolean());
    }

    [Fact]
    public async Task GivenTheSwitch_WhenTurningStreaksOffAndOn_ThenTheSettingsFollow()
    {
        var member = await NewMemberAsync(fixture);
        using var off = await SetAsync(member, false);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await BodyAsync(off)).GetProperty("streaksEnabled").GetBoolean());
        Assert.False((await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me/settings", member.Token))).GetProperty("streaksEnabled").GetBoolean());
        using var on = await SetAsync(member, true);
        Assert.True((await BodyAsync(on)).GetProperty("streaksEnabled").GetBoolean());
    }

    [Fact]
    public async Task GivenNoLoginOrNoValue_WhenCallingTheSettings_ThenTheRequestIsRefused()
    {
        using var anonymous = await SendAsync(fixture, HttpMethod.Get, "/api/me/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var member = await NewMemberAsync(fixture);
        using var empty = await SendAsync(fixture, HttpMethod.Put, "/api/me/settings", member.Token, new { });
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest, "settings_invalid");
    }

    [Fact]
    public async Task GivenARunningStreak_WhenTheOwnerTurnsStreaksOff_ThenNeitherTheOwnerNorVisitorsSeeAStreak()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        var visitor = await NewMemberAsync(fixture);
        Assert.Equal(JsonValueKind.Object, (await TankStreaksAsync(tank, visitor.Token)).GetProperty("waterChange").ValueKind);
        await SetAsync(owner, false);

        foreach (var token in new[] { owner.Token, visitor.Token, null })
        {
            var streaks = await TankStreaksAsync(tank, token);
            Assert.Equal(JsonValueKind.Null, streaks.GetProperty("waterChange").ValueKind);
            Assert.Equal(JsonValueKind.Null, streaks.GetProperty("timeLapse").ValueKind);
            Assert.Empty(streaks.GetProperty("vacations").EnumerateArray());
        }
        var own = await OwnStreaksAsync(owner);
        Assert.False(own.GetProperty("enabled").GetBoolean());
        Assert.Empty(own.GetProperty("tanks").EnumerateArray());
        Assert.Empty(own.GetProperty("vacations").EnumerateArray());
    }

    [Fact]
    public async Task GivenStreaksOn_WhenReadingTheOwnStreaks_ThenTheyAreEnabled()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        var own = await OwnStreaksAsync(owner);
        Assert.True(own.GetProperty("enabled").GetBoolean());
        Assert.Equal(tank, Assert.Single(own.GetProperty("tanks").EnumerateArray()).GetProperty("tankId").GetInt64());
    }

    [Fact]
    public async Task GivenStreaksTurnedOffForAWhile_WhenTurnedOnAgain_ThenTheStreakShowsTheStateItHadAllAlong()
    {
        var (owner, tank) = await OwnerWithHistoryAsync();
        var (control, controlTank) = await OwnerWithHistoryAsync();
        await SetAsync(owner, false);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 15), true);
        await AddWaterChangeAsync(fixture, controlTank, control.ProfileId, At(10, 15), true);
        await SetAsync(owner, true);

        var water = (await TankStreaksAsync(tank, owner.Token)).GetProperty("waterChange");
        var expected = (await TankStreaksAsync(controlTank, control.Token)).GetProperty("waterChange");
        Assert.Equal(expected.GetRawText(), water.GetRawText());
        Assert.True(water.GetProperty("current").GetInt32() >= 3);
    }

    [Fact]
    public async Task GivenAnUnverifiedWaterChange_WhenTheOwnerReadsWithStreaksOff_ThenTheFreezeIsRecordedAsWithStreaksOn()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        var unverified = await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), false);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 14), true);
        await SetAsync(owner, false);

        await OwnStreaksAsync(owner);
        await TankStreaksAsync(tank, owner.Token);

        await using var db = fixture.CreateDbContext();
        var freeze = await db.StreakFreezes.SingleAsync(f => f.TankId == tank);
        Assert.Equal(unverified, freeze.CareEntryId);
        await SetAsync(owner, true);
        var water = (await TankStreaksAsync(tank, owner.Token)).GetProperty("waterChange");
        Assert.Equal(1, water.GetProperty("freezesLeft").GetInt32());
        Assert.Equal(1, await db.StreakFreezes.CountAsync(f => f.TankId == tank));
    }

    [Fact]
    public async Task GivenEndedVacations_WhenTheOwnerTurnsStreaksOff_ThenTheProfileShowsNoVacationShare()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await AddVacationAsync(fixture, owner.ProfileId, today.AddDays(-30), today.AddDays(-21));
        Assert.Equal(JsonValueKind.Object, (await VacationShareAsync(owner.ProfileId, viewer.Token)).ValueKind);
        Assert.Equal(JsonValueKind.Object, (await VacationShareAsync(owner.ProfileId, owner.Token)).ValueKind);

        await SetAsync(owner, false);

        Assert.Equal(JsonValueKind.Null, (await VacationShareAsync(owner.ProfileId, viewer.Token)).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await VacationShareAsync(owner.ProfileId, owner.Token)).ValueKind);
    }

    [Fact]
    public async Task GivenARunningStreak_WhenStreaksAreOff_ThenTheDueCareCarriesNoStreakWindow()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 8), true);
        const string due = "/api/me/care/due?date=2026-10-15&utcOffsetMinutes=0";

        var on = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, due, owner.Token));
        Assert.Equal(JsonValueKind.Object, on.GetProperty("items").EnumerateArray().Single().GetProperty("streak").ValueKind);

        await SetAsync(owner, false);
        var off = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, due, owner.Token));
        var item = off.GetProperty("items").EnumerateArray().Single();
        Assert.False(item.TryGetProperty("streak", out var streak) && streak.ValueKind == JsonValueKind.Object);
    }
}
