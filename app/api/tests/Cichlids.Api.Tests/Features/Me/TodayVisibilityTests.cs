using System.Net;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Me;

[Collection("Today")]
public sealed class TodayVisibilityTests(ApiFixture fixture)
{
    private async Task<System.Text.Json.JsonElement> TodayAsync(string token)
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, "/api/me/today?date=2026-10-15", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }

    [Fact]
    public async Task GivenTheTankOfTheWeek_WhenItsOwnerSwitchesItToPrivateAfterTheRankingsWereCached_ThenTodayDropsItAtOnce()
    {
        var viewer = await NewMemberAsync(fixture);
        await using var db = fixture.CreateDbContext();
        await db.Profiles.Where(p => p.Id == viewer.ProfileId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Interests, new[] { "lake_victoria" }));
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.Region, TankRegion.LakeVictoria));
        await db.Ratings.AddRangeAsync(Enumerable.Range(0, 12).Select(_ => new Cichlids.Domain.Entities.Rating { TankId = tank, Stars = 5 }));
        await db.SaveChangesAsync();

        // Fills the cache of the rankings with the tank in the hall of its region.
        var before = await TodayAsync(viewer.Token);
        var weekly = before.GetProperty("tankOfTheWeek");
        Assert.Equal(System.Text.Json.JsonValueKind.Object, weekly.ValueKind);
        var weeklyId = weekly.GetProperty("tank").GetProperty("id").GetInt64();

        await db.Tanks.Where(t => t.Id == weeklyId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private));
        try
        {
            var after = await TodayAsync(viewer.Token);
            if (after.GetProperty("tankOfTheWeek") is { ValueKind: System.Text.Json.JsonValueKind.Object } next)
            {
                Assert.NotEqual(weeklyId, next.GetProperty("tank").GetProperty("id").GetInt64());
            }
        }
        finally
        {
            await db.Tanks.Where(t => t.Id == weeklyId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProfileVisibility, TankVisibility.Public).SetProperty(t => t.StreamVisibility, TankVisibility.Public));
            await db.Ratings.Where(r => r.TankId == tank).ExecuteDeleteAsync();
            await RemoveTanksAsync(fixture, tank);
        }
    }

    [Fact]
    public async Task GivenAPrivateTankOfTheMember_WhenOpeningToday_ThenOnlyTheirOwnTodayListsIt()
    {
        var owner = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private);

        var own = await TodayAsync(owner.Token);
        var foreign = await TodayAsync(other.Token);

        Assert.Contains(own.GetProperty("tanks").EnumerateArray(), t => t.GetProperty("id").GetInt64() == tank);
        Assert.DoesNotContain(foreign.GetProperty("tanks").EnumerateArray(), t => t.GetProperty("id").GetInt64() == tank);
        await RemoveTanksAsync(fixture, tank);
    }
}
