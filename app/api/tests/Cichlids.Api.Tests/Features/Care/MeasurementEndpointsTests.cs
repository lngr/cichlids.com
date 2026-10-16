using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class MeasurementEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care" || e.AggregateType == "tank").ToListAsync();
        db.OutboxEvents.RemoveRange(events.Where(e =>
        {
            using var payload = JsonDocument.Parse(e.Payload);
            return payload.RootElement.TryGetProperty("tankId", out var id) && _tanks.Contains(id.GetInt64());
        }));
        await db.SaveChangesAsync();
        await db.CareEntries.Where(e => _tanks.Contains(e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => _tanks.Contains(t.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GivenOwnTank_WhenLoggingSeveralParameters_ThenOneMeasurementEntryKeepsInputAndNormalizedValues()
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new
        {
            kind = "measurement",
            values = new object[]
            {
                new { parameter = "ph", value = 7.2, origin = "test_kit" },
                new { parameter = "no3", value = 20, unit = "mg_per_liter", origin = "test_kit" },
                new { parameter = "temperature", value = 77, unit = "fahrenheit", origin = "probe" },
            },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await BodyAsync(response);
        Assert.Equal("measurement", entry.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("amount").ValueKind);
        var values = entry.GetProperty("values").EnumerateArray().ToDictionary(v => v.GetProperty("parameter").GetString()!);
        Assert.Equal(3, values.Count);
        Assert.Equal("none", values["ph"].GetProperty("unit").GetString());
        Assert.Equal(7.2m, values["ph"].GetProperty("normalizedValue").GetDecimal());
        Assert.Equal("test_kit", values["ph"].GetProperty("origin").GetString());
        Assert.Equal("mg_per_liter", values["no3"].GetProperty("canonicalUnit").GetString());
        Assert.Equal(77m, values["temperature"].GetProperty("value").GetDecimal());
        Assert.Equal("fahrenheit", values["temperature"].GetProperty("unit").GetString());
        Assert.Equal(25m, values["temperature"].GetProperty("normalizedValue").GetDecimal());
        Assert.Equal("celsius", values["temperature"].GetProperty("canonicalUnit").GetString());
        Assert.Equal("probe", values["temperature"].GetProperty("origin").GetString());
    }

    [Theory]
    [InlineData("temperature", 77, "fahrenheit", 25, "celsius")]
    [InlineData("temperature", 26.5, "celsius", 26.5, "celsius")]
    [InlineData("temperature", 26.5, null, 26.5, "celsius")]
    [InlineData("kh", 3, "dkh", 3, "dkh")]
    [InlineData("kh", 53.544, "ppm", 3, "dkh")]
    [InlineData("gh", 7, "dgh", 7, "dgh")]
    [InlineData("gh", 17.848, "ppm", 1, "dgh")]
    [InlineData("no2", 0.25, "mg_per_liter", 0.25, "mg_per_liter")]
    [InlineData("nh4", 0, null, 0, "mg_per_liter")]
    [InlineData("po4", 1.5, "mg_per_liter", 1.5, "mg_per_liter")]
    [InlineData("conductivity", 550, "microsiemens_per_cm", 550, "microsiemens_per_cm")]
    [InlineData("tds", 300, "ppm", 300, "ppm")]
    [InlineData("ph", 0, null, 0, "none")]
    [InlineData("ph", 14, "none", 14, "none")]
    public async Task GivenParameterAndUnit_WhenLogged_ThenValueIsNormalizedToTheCanonicalUnit(
        string parameter, double value, string? unit, double normalized, string canonical)
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token,
            new { kind = "measurement", values = new[] { new { parameter, value = (decimal)value, unit, origin = (string?)null } } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single((await BodyAsync(response)).GetProperty("values").EnumerateArray());
        Assert.Equal((decimal)value, stored.GetProperty("value").GetDecimal());
        Assert.Equal((decimal)normalized, stored.GetProperty("normalizedValue").GetDecimal());
        Assert.Equal(canonical, stored.GetProperty("canonicalUnit").GetString());
        Assert.Equal(JsonValueKind.Null, stored.GetProperty("origin").ValueKind);
    }

    [Theory]
    [InlineData("{\"kind\":\"measurement\"}", "care_measurement_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[]}", "care_measurement_invalid")]
    [InlineData("{\"kind\":\"water_change\",\"values\":[{\"parameter\":\"ph\",\"value\":7}]}", "care_measurement_invalid")]
    [InlineData("{\"values\":[{\"parameter\":\"ph\",\"value\":7}]}", "care_measurement_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":7},{\"parameter\":\"ph\",\"value\":8}]}", "care_measurement_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":7}],\"amount\":{\"value\":5,\"unit\":\"liter\"}}", "care_request_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"salinity\",\"value\":1}]}", "care_parameter_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":14.01}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":-0.01}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"temperature\",\"value\":113.5,\"unit\":\"fahrenheit\"}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"temperature\",\"value\":45.01,\"unit\":\"celsius\"}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"no3\",\"value\":-1,\"unit\":\"mg_per_liter\"}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"kh\",\"value\":51,\"unit\":\"dkh\"}]}", "care_value_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":7,\"unit\":\"celsius\"}]}", "care_unit_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"kh\",\"value\":7,\"unit\":\"dgh\"}]}", "care_unit_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"gh\",\"value\":7,\"unit\":\"dkh\"}]}", "care_unit_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"temperature\",\"value\":7,\"unit\":\"kelvin\"}]}", "care_unit_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"values\":[{\"parameter\":\"ph\",\"value\":7,\"origin\":\"guess\"}]}", "care_origin_invalid")]
    [InlineData("{\"kind\":\"bogus\"}", "care_kind_invalid")]
    [InlineData("{\"kind\":\"measurement\",\"occurredAt\":\"2999-01-01T00:00:00Z\",\"values\":[{\"parameter\":\"ph\",\"value\":7}]}", "care_time_invalid")]
    public async Task GivenInvalidMeasurement_WhenLogging_ThenCodedProblemAndNoRowsOrOutbox(string json, string code)
    {
        var (token, tank) = await OwnTankAsync();
        await using var db = fixture.CreateDbContext();
        var before = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care");
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, JsonDocument.Parse(json).RootElement);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(before, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care"));
        Assert.Equal(0, await db.CareEntries.CountAsync(e => e.TankId == tank));
    }

    [Fact]
    public async Task GivenMeasurement_WhenCorrectingAndDeleting_ThenValuesAreReplacedRemovedAndEventsCarryThem()
    {
        var (token, tank) = await OwnTankAsync();
        var logged = await LogAsync(token, tank, DateTimeOffset.UtcNow.AddHours(-1), ("ph", 7.0m, "test_kit"), ("no3", 10m, "test_kit"));
        var id = logged.GetProperty("id").GetInt64();
        using var corrected = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { values = new[] { new { parameter = "ph", value = 7.4m, unit = (string?)null, origin = (string?)"meter" } } });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var result = await BodyAsync(corrected);
        Assert.Equal(id, result.GetProperty("id").GetInt64());
        Assert.Equal(logged.GetProperty("occurredAt").GetString(), result.GetProperty("occurredAt").GetString());
        Assert.Equal(logged.GetProperty("recordedAt").GetString(), result.GetProperty("recordedAt").GetString());
        var only = Assert.Single(result.GetProperty("values").EnumerateArray());
        Assert.Equal(7.4m, only.GetProperty("value").GetDecimal());
        Assert.Equal("meter", only.GetProperty("origin").GetString());
        using var noValues = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { amount = (object?)null });
        await AssertProblemAsync(noValues, HttpStatusCode.BadRequest, "care_measurement_invalid");
        using var badRange = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { values = new[] { new { parameter = "ph", value = 15m, unit = (string?)null, origin = (string?)null } } });
        await AssertProblemAsync(badRange, HttpStatusCode.BadRequest, "care_value_invalid");
        using var amountOnMeasurement = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { amount = new { value = 5, unit = "liter" } });
        await AssertProblemAsync(amountOnMeasurement, HttpStatusCode.BadRequest, "care_request_invalid");
        using var waterChange = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { });
        var waterId = (await BodyAsync(waterChange)).GetProperty("id").GetInt64();
        using var valuesOnWaterChange = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{waterId}", token,
            new { values = new[] { new { parameter = "ph", value = 7m, unit = (string?)null, origin = (string?)null } } });
        await AssertProblemAsync(valuesOnWaterChange, HttpStatusCode.BadRequest, "care_measurement_invalid");
        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Single((await BodyAsync(deleted)).GetProperty("values").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.care_measurement_value WHERE care_entry_id = {id}").SingleAsync());
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care" && e.AggregateId == id.ToString()).OrderBy(e => e.OccurredAt).ToListAsync();
        Assert.Equal(new[] { "care.logged", "care.updated", "care.deleted" }, events.Select(e => e.EventType));
        var expected = new[] { new[] { "no3", "ph" }, new[] { "ph" }, new[] { "ph" } };
        for (var i = 0; i < events.Count; i++)
        {
            var payload = JsonDocument.Parse(events[i].Payload).RootElement;
            Assert.Equal("measurement", payload.GetProperty("kind").GetString());
            Assert.Equal(tank, payload.GetProperty("tankId").GetInt64());
            var carried = payload.GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(expected[i], carried.Select(v => v.GetProperty("parameter").GetString()!).Order());
            Assert.All(carried, v => Assert.True(v.TryGetProperty("normalizedValue", out _) && v.TryGetProperty("unit", out _) && v.TryGetProperty("origin", out _)));
        }
    }

    [Fact]
    public async Task GivenMeasurementsAndWaterChange_WhenListing_ThenEntriesIncludeTheirValues()
    {
        var (token, tank) = await OwnTankAsync();
        var when = DateTimeOffset.UtcNow.AddDays(-2);
        await LogAsync(token, tank, when, ("kh", 4m, null));
        using var water = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { occurredAt = when.AddHours(1) });
        Assert.Equal(HttpStatusCode.Created, water.StatusCode);
        using var list = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var items = (await BodyAsync(list)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("water_change", items[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("values").ValueKind);
        Assert.Equal("measurement", items[1].GetProperty("kind").GetString());
        var value = Assert.Single(items[1].GetProperty("values").EnumerateArray());
        Assert.Equal("kh", value.GetProperty("parameter").GetString());
        Assert.Equal("dkh", value.GetProperty("canonicalUnit").GetString());
    }

    [Fact]
    public async Task GivenMeasurementsOnThreeDays_WhenReadingASeries_ThenPointsAreOldestFirstWithinTheRangeForOneParameter()
    {
        var (token, tank) = await OwnTankAsync();
        var (otherToken, otherTank) = await OwnTankAsync();
        var day = DateTimeOffset.UtcNow.AddDays(-10);
        await LogAsync(token, tank, day.AddDays(2), ("ph", 7.6m, "test_kit"), ("no3", 30m, null));
        await LogAsync(token, tank, day, ("ph", 7.0m, "meter"), ("no3", 10m, null));
        await LogAsync(token, tank, day.AddDays(1), ("ph", 7.3m, null));
        await LogAsync(otherToken, otherTank, day, ("ph", 5m, null));
        using var all = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=ph", token);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var series = await BodyAsync(all);
        Assert.Equal("ph", series.GetProperty("parameter").GetString());
        Assert.Equal("none", series.GetProperty("unit").GetString());
        var points = series.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(new[] { 7.0m, 7.3m, 7.6m }, points.Select(p => p.GetProperty("value").GetDecimal()));
        var times = points.Select(p => p.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.Order(), times);
        Assert.Equal("meter", points[0].GetProperty("origin").GetString());
        Assert.Equal(JsonValueKind.Null, points[1].GetProperty("origin").ValueKind);
        Assert.Equal(7.0m, points[0].GetProperty("inputValue").GetDecimal());
        Assert.Equal("none", points[0].GetProperty("inputUnit").GetString());
        var from = Uri.EscapeDataString(day.AddHours(12).ToString("o"));
        var to = Uri.EscapeDataString(day.AddDays(1).AddHours(12).ToString("o"));
        using var ranged = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=ph&from={from}&to={to}", token);
        Assert.Equal(new[] { 7.3m }, (await BodyAsync(ranged)).GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        using var nitrate = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=no3", token);
        var nitrateSeries = await BodyAsync(nitrate);
        Assert.Equal("mg_per_liter", nitrateSeries.GetProperty("unit").GetString());
        Assert.Equal(new[] { 10m, 30m }, nitrateSeries.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        using var none = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=tds", token);
        Assert.Empty((await BodyAsync(none)).GetProperty("points").EnumerateArray());
    }

    [Fact]
    public async Task GivenMeasurementsOfSeveralParameters_WhenReadingAllSeries_ThenEachParameterWithPointsIsListedOldestFirstWithinTheRange()
    {
        var (token, tank) = await OwnTankAsync();
        var (otherToken, otherTank) = await OwnTankAsync();
        var day = DateTimeOffset.UtcNow.AddDays(-10);
        await LogAsync(token, tank, day.AddDays(2), ("ph", 7.6m, "test_kit"), ("no3", 30m, null));
        await LogAsync(token, tank, day, ("ph", 7.0m, "meter"), ("no3", 10m, null));
        await LogAsync(token, tank, day.AddDays(1), ("ph", 7.3m, null));
        await LogAsync(otherToken, otherTank, day, ("tds", 200m, null));
        using var response = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series/all", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var series = (await BodyAsync(response)).GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(new[] { "ph", "no3" }.Order(), series.Select(s => s.GetProperty("parameter").GetString()!).Order());
        var ph = series.Single(s => s.GetProperty("parameter").GetString() == "ph");
        Assert.Equal("none", ph.GetProperty("unit").GetString());
        Assert.Equal(new[] { 7.0m, 7.3m, 7.6m }, ph.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        var no3 = series.Single(s => s.GetProperty("parameter").GetString() == "no3");
        Assert.Equal("mg_per_liter", no3.GetProperty("unit").GetString());
        Assert.Equal(new[] { 10m, 30m }, no3.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        var from = Uri.EscapeDataString(day.AddHours(12).ToString("o"));
        var to = Uri.EscapeDataString(day.AddDays(1).AddHours(12).ToString("o"));
        using var ranged = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series/all?from={from}&to={to}", token);
        var inRange = Assert.Single((await BodyAsync(ranged)).GetProperty("series").EnumerateArray());
        Assert.Equal("ph", inRange.GetProperty("parameter").GetString());
        Assert.Equal(new[] { 7.3m }, inRange.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        using var empty = await SendAsync(HttpMethod.Get, $"/api/tanks/{otherTank}/care/series/all?from={from}&to={from}", otherToken);
        Assert.Empty((await BodyAsync(empty)).GetProperty("series").EnumerateArray());
    }

    [Theory]
    [InlineData("?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "care_range_invalid")]
    [InlineData("?from=yesterday", "care_request_invalid")]
    public async Task GivenInvalidAllSeriesQuery_WhenReading_ThenCodedProblem(string query, string code)
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series/all{query}", token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task GivenMissingTank_WhenReadingAllSeries_ThenCoded404()
    {
        var (owner, _) = await OwnTankAsync();
        using var missing = await SendAsync(HttpMethod.Get, "/api/tanks/9223372036854775807/care/series/all", owner);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "care_not_found");
    }

    [Theory]
    [InlineData("/api/tanks/1/care/series/allx")]
    [InlineData("/api/tanks/1/care/series/all/1")]
    public async Task GivenSimilarAllSeriesRoute_WhenItFails_ThenNoCareProblemCode(string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("care_", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "care_parameter_invalid")]
    [InlineData("?parameter=salinity", "care_parameter_invalid")]
    [InlineData("?parameter=ph&from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "care_range_invalid")]
    [InlineData("?parameter=ph&from=yesterday", "care_request_invalid")]
    public async Task GivenInvalidSeriesQuery_WhenReading_ThenCodedProblem(string query, string code)
    {
        var (token, tank) = await OwnTankAsync();
        using var response = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series{query}", token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task GivenOtherCaller_WhenUsingMeasurementRoutes_ThenCoded404AndNoEvents()
    {
        var (owner, tank) = await OwnTankAsync();
        var logged = await LogAsync(owner, tank, DateTimeOffset.UtcNow, ("ph", 7m, null));
        var id = logged.GetProperty("id").GetInt64();
        var other = TestTokens.Create(Guid.NewGuid().ToString(), $"care-{Guid.NewGuid():N}"[..24]);
        await using var db = fixture.CreateDbContext();
        var count = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care");
        foreach (var token in new[] { other, null })
        {
            using var series = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=ph", token);
            Assert.Equal(HttpStatusCode.OK, series.StatusCode);
        }
        var body = new { kind = "measurement", values = new[] { new { parameter = "ph", value = 7m } } };
        using var post = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", other, body);
        await AssertProblemAsync(post, HttpStatusCode.NotFound, "care_not_found");
        using var patch = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", other, body);
        await AssertProblemAsync(patch, HttpStatusCode.NotFound, "care_not_found");
        using var delete = await SendAsync(HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}", other);
        await AssertProblemAsync(delete, HttpStatusCode.NotFound, "care_not_found");
        Assert.Equal(count, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care"));
        using var anonymous = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", null, body);
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        using var missing = await SendAsync(HttpMethod.Get, "/api/tanks/9223372036854775807/care/series?parameter=ph", owner);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "care_not_found");
    }

    [Theory]
    [InlineData("/api/tanks/1/care/seriesx")]
    [InlineData("/api/tanks/1/care/series/1")]
    public async Task GivenSimilarRoute_WhenItFails_ThenNoCareProblemCode(string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("care_", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task<JsonElement> LogAsync(string token, long tank, DateTimeOffset at, params (string Parameter, decimal Value, string? Origin)[] values)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new
        {
            kind = "measurement", occurredAt = at,
            values = values.Select(v => new { parameter = v.Parameter, value = v.Value, origin = v.Origin }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<(string Token, long Tank)> OwnTankAsync()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"care-{Guid.NewGuid():N}"[..24]);
        using var response = await SendAsync(HttpMethod.Post, "/api/tanks", token, new { title = "Measurement test", isPublic = true });
        response.EnsureSuccessStatusCode();
        var id = (await BodyAsync(response)).GetProperty("id").GetInt64();
        _tanks.Add(id);
        return (token, id);
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
