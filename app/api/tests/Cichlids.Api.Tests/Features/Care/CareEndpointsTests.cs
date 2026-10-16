using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareEndpointsTests(ApiFixture fixture) : IAsyncLifetime
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
    public async Task GivenOwnTank_WhenLoggingThenCompletingAndDeleting_ThenCareAndOutboxFollowEachWrite()
    {
        var (token, tank) = await OwnTankAsync();
        using var logged = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { });
        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var entry = await BodyAsync(logged);
        var id = entry.GetProperty("id").GetInt64();
        Assert.Equal("water_change", entry.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("amount").ValueKind);
        Assert.InRange(DateTimeOffset.Parse(entry.GetProperty("occurredAt").GetString()!), DateTimeOffset.UtcNow.AddSeconds(-15), DateTimeOffset.UtcNow.AddSeconds(1));
        using var updated = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { amount = new { value = 55, unit = "us_gallon" } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var amount = (await BodyAsync(updated)).GetProperty("amount");
        Assert.Equal(55m, amount.GetProperty("value").GetDecimal());
        Assert.Equal(208.197648120m, amount.GetProperty("liters").GetDecimal());
        using var deleted = await SendAsync(HttpMethod.Delete, $"/api/tanks/{tank}/care/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var list = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Empty((await BodyAsync(list)).GetProperty("items").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care" && e.AggregateId == id.ToString()).OrderBy(e => e.OccurredAt).ToListAsync();
        Assert.Equal(new[] { "care.logged", "care.updated", "care.deleted" }, events.Select(e => e.EventType));
        foreach (var evt in events)
        {
            var payload = JsonDocument.Parse(evt.Payload).RootElement;
            Assert.Equal(tank, payload.GetProperty("tankId").GetInt64());
            Assert.Equal(id, payload.GetProperty("careEntryId").GetInt64());
            Assert.True(payload.GetProperty("authorProfileId").GetInt64() > 0);
        }
    }

    [Fact]
    public async Task GivenOtherCaller_WhenReadingOrWritingCare_ThenCoded404AndNoCareEvents()
    {
        var (owner, tank) = await OwnTankAsync();
        using var logged = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", owner, new { });
        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var id = (await BodyAsync(logged)).GetProperty("id").GetInt64();
        var other = TestTokens.Create(Guid.NewGuid().ToString(), $"care-{Guid.NewGuid():N}"[..24]);
        foreach (var token in new[] { other, null })
        {
            using var read = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care", token);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }
        await using var db = fixture.CreateDbContext();
        var count = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var refused = await SendAsync(method, $"/api/tanks/{tank}/care{(method == HttpMethod.Post ? "" : $"/{id}")}", other, new { });
            await AssertProblemAsync(refused, HttpStatusCode.NotFound, "care_not_found");
        }
        Assert.Equal(count, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care"));
        using var missing = await SendAsync(HttpMethod.Get, "/api/tanks/9223372036854775807/care", owner);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "care_not_found");
    }

    [Theory]
    [InlineData("liter", 42, 42d)]
    [InlineData("us_gallon", 10, 37.854117840)]
    [InlineData("percent", 25, null)]
    public async Task GivenAmountAndPastTime_WhenLogged_ThenOriginalUnitAndNormalizedVolumePersist(string unit, decimal value, double? liters)
    {
        var (token, tank) = await OwnTankAsync();
        var past = DateTimeOffset.UtcNow.AddDays(-5);
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { occurredAt = past, amount = new { value, unit } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await BodyAsync(response);
        Assert.InRange(DateTimeOffset.Parse(entry.GetProperty("occurredAt").GetString()!), past.AddMilliseconds(-1), past.AddMilliseconds(1));
        Assert.True(DateTimeOffset.Parse(entry.GetProperty("recordedAt").GetString()!) > past);
        var amount = entry.GetProperty("amount");
        Assert.Equal(value, amount.GetProperty("value").GetDecimal());
        Assert.Equal(unit, amount.GetProperty("unit").GetString());
        if (liters is null) Assert.Equal(JsonValueKind.Null, amount.GetProperty("liters").ValueKind);
        else Assert.Equal((decimal)liters, amount.GetProperty("liters").GetDecimal());
    }

    [Theory]
    [InlineData("{\"occurredAt\":\"2999-01-01T00:00:00Z\"}", "care_time_invalid")]
    [InlineData("{\"amount\":{\"value\":0,\"unit\":\"liter\"}}", "care_amount_invalid")]
    [InlineData("{\"amount\":{\"value\":-2,\"unit\":\"us_gallon\"}}", "care_amount_invalid")]
    [InlineData("{\"amount\":{\"value\":101,\"unit\":\"percent\"}}", "care_amount_invalid")]
    [InlineData("{\"amount\":{\"value\":1000001,\"unit\":\"liter\"}}", "care_amount_invalid")]
    [InlineData("{\"amount\":{\"value\":1,\"unit\":\"bucket\"}}", "care_request_invalid")]
    [InlineData("{\"kind\":\"bogus\"}", "care_kind_invalid")]
    public async Task GivenInvalidInput_WhenLogging_ThenCodedProblemAndNoOutbox(string json, string code)
    {
        var (token, tank) = await OwnTankAsync();
        await using var db = fixture.CreateDbContext();
        var before = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care");
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, JsonDocument.Parse(json).RootElement);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(before, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care"));
    }

    [Fact]
    public async Task GivenSeveralEntries_WhenPaging_ThenNewestFirstWithStableTiesAndTotal()
    {
        var (token, tank) = await OwnTankAsync();
        var time = DateTimeOffset.UtcNow.AddDays(-1);
        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { occurredAt = time });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await BodyAsync(response)).GetProperty("id").GetInt64());
        }
        using var page = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care?offset=1&limit=1", token);
        var body = await BodyAsync(page);
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(ids[1], Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenOwnEntry_WhenCorrectingClearingOrRefusingAmount_ThenIdentityAndTimesStayStable()
    {
        var (token, tank) = await OwnTankAsync();
        using var logged = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { amount = new { value = 20, unit = "liter" } });
        var original = await BodyAsync(logged);
        var id = original.GetProperty("id").GetInt64();
        using var corrected = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { amount = new { value = 30, unit = "percent" } });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var result = await BodyAsync(corrected);
        Assert.Equal(id, result.GetProperty("id").GetInt64());
        Assert.Equal(original.GetProperty("occurredAt").GetString(), result.GetProperty("occurredAt").GetString());
        Assert.Equal(original.GetProperty("recordedAt").GetString(), result.GetProperty("recordedAt").GetString());
        Assert.Equal(30, result.GetProperty("amount").GetProperty("value").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("amount").GetProperty("liters").ValueKind);
        using var invalid = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { amount = new { value = 0, unit = "liter" } });
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "care_amount_invalid");
        using var cleared = await SendAsync(HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { amount = (object?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(cleared)).GetProperty("amount").ValueKind);
        var (_, otherTank) = await OwnTankAsync();
        using var crossTank = await SendAsync(HttpMethod.Patch, $"/api/tanks/{otherTank}/care/{id}", token,
            new { amount = new { value = 10, unit = "liter" } });
        await AssertProblemAsync(crossTank, HttpStatusCode.NotFound, "care_not_found");
    }

    [Fact]
    public async Task GivenClockSkewAndDeletedTank_WhenLogging_ThenOnlySmallSkewIsAccepted()
    {
        var (token, tank) = await OwnTankAsync();
        using var withinSkew = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token,
            new { occurredAt = DateTimeOffset.UtcNow.AddSeconds(30) });
        Assert.Equal(HttpStatusCode.Created, withinSkew.StatusCode);
        using var beyondSkew = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token,
            new { occurredAt = DateTimeOffset.UtcNow.AddMinutes(3) });
        await AssertProblemAsync(beyondSkew, HttpStatusCode.BadRequest, "care_time_invalid");
        using var anonymous = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", null, new { });
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        await using var db = fixture.CreateDbContext();
        await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
        using var deleted = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token, new { });
        await AssertProblemAsync(deleted, HttpStatusCode.NotFound, "care_not_found");
        using var hidden = await SendAsync(HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "care_not_found");
    }

    [Theory]
    [InlineData("55.123456789123456789123456789")]
    [InlineData("0.0000000000000000000000000001")]
    public async Task GivenPreciseGallons_WhenLogged_ThenDecimalNormalizationAndInputArePreserved(string input)
    {
        var (token, tank) = await OwnTankAsync();
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        using var response = await SendAsync(HttpMethod.Post, $"/api/tanks/{tank}/care", token,
            new { amount = new { value, unit = "us_gallon" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var amount = (await BodyAsync(response)).GetProperty("amount");
        Assert.Equal(value, amount.GetProperty("value").GetDecimal());
        Assert.Equal(value * Cichlids.Domain.TankMeasurements.LitersPerUsGallon, amount.GetProperty("liters").GetDecimal());
    }

    [Theory]
    [InlineData("/api/species/careful-unknown-species")]
    [InlineData("/api/tanks/1/careless")]
    [InlineData("/api/pictures/care")]
    public async Task GivenOtherRouteContainingCare_WhenItFails_ThenNoCareProblemCode(string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("care_", body, StringComparison.Ordinal);
    }

    private async Task<(string Token, long Tank)> OwnTankAsync()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"care-{Guid.NewGuid():N}"[..24]);
        using var response = await SendAsync(HttpMethod.Post, "/api/tanks", token, new { title = "Care test", isPublic = true });
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
