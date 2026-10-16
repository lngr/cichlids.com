using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareRemindersTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Date = "2026-10-15";
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await RemoveTanksAsync(db, _tanks);
    }

    private async Task<(string Token, long Tank)> OwnTankAsync()
    {
        var own = await CareTestSupport.OwnTankAsync(fixture.Client);
        _tanks.Add(own.Tank);
        return own;
    }

    private static string PlanUrl(long tank) => $"/api/tanks/{tank}/care/plan?date={Date}";

    [Fact]
    public async Task GivenAFreshTank_WhenReadingThePlanAndTheDueCare_ThenRemindersAreOn()
    {
        var (token, tank) = await OwnTankAsync();
        using var plan = await SendAsync(fixture.Client, HttpMethod.Get, PlanUrl(tank), token);
        Assert.True((await BodyAsync(plan)).GetProperty("remindersEnabled").GetBoolean());
        using var due = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/care/due?date={Date}", token);
        var item = (await BodyAsync(due)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("tankId").GetInt64() == tank);
        Assert.True(item.GetProperty("remindersEnabled").GetBoolean());
    }

    [Fact]
    public async Task GivenOwnTank_WhenSwitchingRemindersOffAndOn_ThenPlanAndDueCareFollowAndOneEventIsRecordedEach()
    {
        var (token, tank) = await OwnTankAsync();
        using var off = await SendAsync(fixture.Client, HttpMethod.Patch, PlanUrl(tank), token, new { remindersEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var plan = await BodyAsync(off);
        Assert.False(plan.GetProperty("remindersEnabled").GetBoolean());
        Assert.Equal(2, plan.GetProperty("rules").GetArrayLength());
        using var due = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/care/due?date={Date}", token);
        var item = (await BodyAsync(due)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("tankId").GetInt64() == tank);
        Assert.False(item.GetProperty("remindersEnabled").GetBoolean());

        using var again = await SendAsync(fixture.Client, HttpMethod.Patch, PlanUrl(tank), token, new { remindersEnabled = false });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var on = await SendAsync(fixture.Client, HttpMethod.Patch, PlanUrl(tank), token, new { remindersEnabled = true });
        Assert.True((await BodyAsync(on)).GetProperty("remindersEnabled").GetBoolean());

        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "tank", tank);
        var changes = events.Where(e => e.Type == "tank.care_reminders_changed").ToList();
        Assert.Equal(2, changes.Count);
        Assert.False(changes[0].Payload.GetProperty("enabled").GetBoolean());
        Assert.True(changes[1].Payload.GetProperty("enabled").GetBoolean());
        Assert.Equal(tank, changes[0].Payload.GetProperty("tankId").GetInt64());
    }

    [Fact]
    public async Task GivenAnotherMembersTank_WhenSwitchingReminders_ThenCodedNotFoundAndNothingChanges()
    {
        var (_, tank) = await OwnTankAsync();
        var (stranger, _) = await OwnTankAsync();
        using var change = await SendAsync(fixture.Client, HttpMethod.Patch, PlanUrl(tank), stranger, new { remindersEnabled = false });
        await AssertProblemAsync(change, HttpStatusCode.NotFound, "care_not_found");
        using var anonymous = await SendAsync(fixture.Client, HttpMethod.Patch, PlanUrl(tank), null, new { remindersEnabled = false });
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Tanks.Where(t => t.Id == tank).Select(t => t.CareRemindersEnabled).SingleAsync());
        Assert.DoesNotContain(await EventsAsync(db, "tank", tank), e => e.Type == "tank.care_reminders_changed");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"remindersEnabled\":null}")]
    public async Task GivenNoSwitchValue_WhenChangingThePlan_ThenCodedProblemAndNoEvent(string json)
    {
        var (token, tank) = await OwnTankAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, PlanUrl(tank)) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "care_plan_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.DoesNotContain(await EventsAsync(db, "tank", tank), e => e.Type == "tank.care_reminders_changed");
    }
}
