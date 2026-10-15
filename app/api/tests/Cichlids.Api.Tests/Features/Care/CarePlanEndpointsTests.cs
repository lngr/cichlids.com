using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CarePlanEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Date = "2026-10-15";
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care" || e.AggregateType == "care_plan" || e.AggregateType == "tank").ToListAsync();
        db.OutboxEvents.RemoveRange(events.Where(e =>
        {
            using var payload = JsonDocument.Parse(e.Payload);
            return payload.RootElement.TryGetProperty("tankId", out var id) && _tanks.Contains(id.GetInt64());
        }));
        await db.SaveChangesAsync();
        await db.CarePlanRules.Where(r => _tanks.Contains(r.TankId)).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => _tanks.Contains(e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => _tanks.Contains(t.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GivenFreshTank_WhenReadingThePlan_ThenTheDefaultWaterChangeRuleHasNeverBeenLoggedAndMeasurementHasNoRule()
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Get, PlanUrl(tank), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rules = (await BodyAsync(response)).GetProperty("rules").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!);
        var water = rules["water_change"];
        Assert.Equal(7, water.GetProperty("rhythmDays").GetInt32());
        Assert.True(water.GetProperty("enabled").GetBoolean());
        Assert.True(water.GetProperty("isDefault").GetBoolean());
        Assert.Equal("cichlid_default", water.GetProperty("reason").GetProperty("code").GetString());
        Assert.Equal("never_logged", water.GetProperty("due").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, water.GetProperty("due").GetProperty("dueDate").ValueKind);
        var measurement = rules["measurement"];
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("rhythmDays").ValueKind);
        Assert.False(measurement.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("due").ValueKind);
    }

    [Theory]
    [InlineData(10, "overdue", 3)]
    [InlineData(7, "due_today", 0)]
    [InlineData(4, "due_in", 3)]
    public async Task GivenALastWaterChange_WhenReadingThePlan_ThenTheDueStateFollowsTheDefaultRhythm(int daysAgo, string state, int days)
    {
        var (token, tank) = await OwnTankAsync();
        await LogAsync(token, tank, "water_change", Noon.AddDays(-daysAgo));
        var water = (await PlanAsync(token, tank))["water_change"];
        Assert.Equal(state, water.GetProperty("due").GetProperty("state").GetString());
        Assert.Equal(days, water.GetProperty("due").GetProperty("days").GetInt32());
        Assert.Equal(Noon.AddDays(-daysAgo + 7).ToString("yyyy-MM-dd"), water.GetProperty("due").GetProperty("dueDate").GetString());
        Assert.Equal("cichlid_default", water.GetProperty("reason").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GivenTheMembersUtcOffset_WhenReadingThePlan_ThenTheLastEntryCountsOnTheLocalDay()
    {
        var (token, tank) = await OwnTankAsync();
        await LogAsync(token, tank, "water_change", new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.Zero));
        var utc = (await PlanAsync(token, tank))["water_change"].GetProperty("due");
        Assert.Equal("due_today", utc.GetProperty("state").GetString());
        var ahead = (await PlanAsync(token, tank, "&utcOffsetMinutes=120"))["water_change"].GetProperty("due");
        Assert.Equal("due_in", ahead.GetProperty("state").GetString());
        Assert.Equal(1, ahead.GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task GivenOwnTank_WhenChangingDisablingAndAddingRules_ThenThePlanAndTheOutboxFollow()
    {
        var (token, tank) = await OwnTankAsync();
        await LogAsync(token, tank, "water_change", Noon.AddDays(-10));
        await LogAsync(token, tank, "measurement", Noon.AddDays(-2), new { parameter = "ph", value = 7.2 });

        using var changed = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), token, new { rhythmDays = 14 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var water = RulesOf(await BodyAsync(changed))["water_change"];
        Assert.Equal(14, water.GetProperty("rhythmDays").GetInt32());
        Assert.False(water.GetProperty("isDefault").GetBoolean());
        Assert.Equal("member_choice", water.GetProperty("reason").GetProperty("code").GetString());
        Assert.Equal("due_in", water.GetProperty("due").GetProperty("state").GetString());
        Assert.Equal(4, water.GetProperty("due").GetProperty("days").GetInt32());

        using var off = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), token, new { enabled = false });
        var disabled = RulesOf(await BodyAsync(off))["water_change"];
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        Assert.Equal(14, disabled.GetProperty("rhythmDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, disabled.GetProperty("due").ValueKind);

        using var on = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), token, new { enabled = true });
        Assert.Equal("due_in", RulesOf(await BodyAsync(on))["water_change"].GetProperty("due").GetProperty("state").GetString());

        using var added = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "measurement"), token, new { rhythmDays = 3 });
        var measurement = RulesOf(await BodyAsync(added))["measurement"];
        Assert.True(measurement.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, measurement.GetProperty("rhythmDays").GetInt32());
        Assert.Equal("member_choice", measurement.GetProperty("reason").GetProperty("code").GetString());
        Assert.Equal("due_in", measurement.GetProperty("due").GetProperty("state").GetString());
        Assert.Equal(1, measurement.GetProperty("due").GetProperty("days").GetInt32());

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.CarePlanRules.CountAsync(r => r.TankId == tank));
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care_plan").ToListAsync();
        var mine = events.Where(e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("tankId").GetInt64() == tank).ToList();
        Assert.Equal(4, mine.Count);
        Assert.All(mine, e => Assert.Equal("care_plan.updated", e.EventType));
        var last = JsonDocument.Parse(mine.OrderByDescending(e => e.OccurredAt).First().Payload).RootElement;
        Assert.Equal("measurement", last.GetProperty("kind").GetString());
        Assert.Equal(3, last.GetProperty("rhythmDays").GetInt32());
        Assert.True(last.GetProperty("enabled").GetBoolean());
        Assert.Equal("member_choice", last.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task GivenAnUnchangedDefaultRhythm_WhenSavingIt_ThenTheReasonStaysTheDefaultOne()
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), token, new { rhythmDays = 7 });
        var water = RulesOf(await BodyAsync(response))["water_change"];
        Assert.Equal("cichlid_default", water.GetProperty("reason").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GivenNoStoredMeasurementRule_WhenSwitchingItOff_ThenNothingIsStored()
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "measurement"), token, new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.CarePlanRules.CountAsync(r => r.TankId == tank));
    }

    [Theory]
    [InlineData("{\"rhythmDays\":0}", "care_plan_invalid")]
    [InlineData("{\"rhythmDays\":91}", "care_plan_invalid")]
    [InlineData("{\"rhythmDays\":-1}", "care_plan_invalid")]
    [InlineData("{}", "care_plan_invalid")]
    [InlineData("{\"enabled\":true}", "care_plan_invalid", "measurement")]
    [InlineData("{\"rhythmDays\":7}", "care_kind_invalid", "feeding")]
    [InlineData("{\"rhythmDays\":\"x\"}", "care_request_invalid")]
    public async Task GivenInvalidInput_WhenChangingARule_ThenCodedProblemAndNoOutbox(string json, string code, string kind = "water_change")
    {
        var (token, tank) = await OwnTankAsync();
        await using var db = fixture.CreateDbContext();
        var before = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care_plan");
        using var request = new HttpRequestMessage(HttpMethod.Patch, RuleUrl(tank, kind)) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.Client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(before, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care_plan"));
        Assert.Equal(0, await db.CarePlanRules.CountAsync(r => r.TankId == tank));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=2026-2-30")]
    [InlineData("?date=2026-10-15&utcOffsetMinutes=900")]
    [InlineData("?date=2026-10-15&utcOffsetMinutes=x")]
    public async Task GivenAnInvalidLocalDate_WhenReadingThePlan_ThenCodedProblem(string query)
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/plan{query}", token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, query.Contains("utcOffsetMinutes=x") ? "care_request_invalid" : "care_date_invalid");
    }

    [Fact]
    public async Task GivenAnotherMembersTank_WhenReadingOrChangingThePlan_ThenCodedNotFound()
    {
        var (_, tank) = await OwnTankAsync();
        var (stranger, _) = await OwnTankAsync();
        using var read = await SendAsync(HttpMethod.Get, PlanUrl(tank), stranger);
        await AssertProblemAsync(read, HttpStatusCode.NotFound, "care_not_found");
        using var anonymousRead = await SendAsync(HttpMethod.Get, PlanUrl(tank), null);
        await AssertProblemAsync(anonymousRead, HttpStatusCode.NotFound, "care_not_found");
        using var change = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), stranger, new { rhythmDays = 3 });
        await AssertProblemAsync(change, HttpStatusCode.NotFound, "care_not_found");
        using var anonymousChange = await SendAsync(HttpMethod.Patch, RuleUrl(tank, "water_change"), null, new { rhythmDays = 3 });
        await AssertProblemAsync(anonymousChange, HttpStatusCode.Unauthorized, "care_auth_required");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.CarePlanRules.CountAsync(r => r.TankId == tank));
    }

    [Fact]
    public async Task GivenSeveralOwnTanks_WhenListingTheDueCare_ThenOverdueComesFirstThenDueTodayThenUpcomingThenNeverLogged()
    {
        var (token, never) = await OwnTankAsync();
        var upcoming = await AddTankAsync(token);
        var today = await AddTankAsync(token);
        var late = await AddTankAsync(token);
        var later = await AddTankAsync(token);
        var switchedOff = await AddTankAsync(token);
        await LogAsync(token, upcoming, "water_change", Noon.AddDays(-5));
        await LogAsync(token, today, "water_change", Noon.AddDays(-7));
        await LogAsync(token, late, "water_change", Noon.AddDays(-9));
        await LogAsync(token, later, "water_change", Noon.AddDays(-12));
        await LogAsync(token, switchedOff, "water_change", Noon.AddDays(-30));
        (await SendAsync(HttpMethod.Patch, RuleUrl(switchedOff, "water_change"), token, new { enabled = false })).Dispose();
        (await SendAsync(HttpMethod.Patch, RuleUrl(late, "measurement"), token, new { rhythmDays = 3 })).Dispose();
        var (stranger, foreign) = await OwnTankAsync();
        await LogAsync(stranger, foreign, "water_change", Noon.AddDays(-40));

        using var response = await SendAsync(HttpMethod.Get, $"/api/me/care/due?date={Date}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await BodyAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            new[] { (later, "water_change", "overdue", 5), (late, "water_change", "overdue", 2), (today, "water_change", "due_today", 0),
                (upcoming, "water_change", "due_in", 2), (never, "water_change", "never_logged", 0), (late, "measurement", "never_logged", 0) },
            items.Select(i => (i.GetProperty("tankId").GetInt64(), i.GetProperty("kind").GetString()!,
                i.GetProperty("due").GetProperty("state").GetString()!, i.GetProperty("due").GetProperty("days").GetInt32())));
        var first = items[0];
        Assert.Equal("Care test", first.GetProperty("tankTitle").GetString());
        Assert.Equal(7, first.GetProperty("rhythmDays").GetInt32());
        Assert.Equal("cichlid_default", first.GetProperty("reason").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GivenNoLoginOrAnInvalidDate_WhenListingTheDueCare_ThenCodedProblem()
    {
        using var anonymous = await SendAsync(HttpMethod.Get, $"/api/me/care/due?date={Date}", null);
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        var (token, _) = await OwnTankAsync();
        using var invalid = await SendAsync(HttpMethod.Get, "/api/me/care/due?date=garbage", token);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "care_date_invalid");
    }

    private static string PlanUrl(long tank, string extra = "") => $"/api/tanks/{tank}/care/plan?date={Date}{extra}";
    private static string RuleUrl(long tank, string kind) => $"/api/tanks/{tank}/care/plan/{kind}?date={Date}";

    private static Dictionary<string, JsonElement> RulesOf(JsonElement plan) =>
        plan.GetProperty("rules").EnumerateArray().ToDictionary(r => r.GetProperty("kind").GetString()!);

    private async Task<Dictionary<string, JsonElement>> PlanAsync(string token, long tank, string extra = "")
    {
        using var response = await SendAsync(HttpMethod.Get, PlanUrl(tank, extra), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return RulesOf(await BodyAsync(response));
    }

    private async Task LogAsync(string token, long tank, string kind, DateTimeOffset at, object? value = null)
    {
        object body = kind == "measurement"
            ? new { kind, occurredAt = at, values = new[] { value ?? new { parameter = "ph", value = 7.0 } } }
            : new { kind, occurredAt = at };
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<long> AddTankAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/tanks", token, new { title = "Care test" });
        response.EnsureSuccessStatusCode();
        var id = (await BodyAsync(response)).GetProperty("id").GetInt64();
        _tanks.Add(id);
        return id;
    }

    private async Task<(string Token, long Tank)> OwnTankAsync()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plan-{Guid.NewGuid():N}"[..24]);
        return (token, await AddTankAsync(token));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await fixture.Client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await BodyAsync(response)).GetProperty("code").GetString());
    }
}
