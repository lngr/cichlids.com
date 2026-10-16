using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareTimeTests(ApiFixture fixture) : IAsyncLifetime
{
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

    private static object Nitrate(decimal value) => new { parameter = "no3", value, unit = (string?)null, origin = (string?)null };

    [Fact]
    public async Task GivenMeasurement_WhenOnlyTheTimeIsSent_ThenTheTimeMovesAndValuesAndRecordedTimeStay()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await LogAsync(fixture.Client, token, tank, new { kind = "measurement", values = new[] { Nitrate(25m) } });
        var earlier = DateTimeOffset.UtcNow.AddDays(-2);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { occurredAt = earlier });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await BodyAsync(response);
        Assert.True(Math.Abs((entry.GetProperty("occurredAt").GetDateTimeOffset() - earlier).TotalMilliseconds) < 1);
        Assert.True(entry.GetProperty("recordedAt").GetDateTimeOffset() > earlier.AddDays(1));
        Assert.Equal(25m, Assert.Single(entry.GetProperty("values").EnumerateArray()).GetProperty("value").GetDecimal());
        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=no3", token);
        var point = Assert.Single((await BodyAsync(series)).GetProperty("points").EnumerateArray());
        Assert.True(Math.Abs((point.GetProperty("occurredAt").GetDateTimeOffset() - earlier).TotalMilliseconds) < 1);
        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "care", id);
        Assert.Equal(new[] { "care.logged", "care.updated" }, events.Select(e => e.Type));
        Assert.True(Math.Abs((events[1].Payload.GetProperty("occurredAt").GetDateTimeOffset() - earlier).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task GivenMeasurement_WhenValuesAndTimeAreSentTogether_ThenBothApply()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await LogAsync(fixture.Client, token, tank, new { kind = "measurement", values = new[] { Nitrate(25m) } });
        var earlier = DateTimeOffset.UtcNow.AddDays(-1);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { occurredAt = earlier, values = new[] { Nitrate(30m) } });
        var entry = await BodyAsync(response);
        Assert.Equal(30m, Assert.Single(entry.GetProperty("values").EnumerateArray()).GetProperty("value").GetDecimal());
        Assert.True(Math.Abs((entry.GetProperty("occurredAt").GetDateTimeOffset() - earlier).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task GivenWaterChangeWithAmount_WhenOnlyTheTimeIsSent_ThenTheAmountStays()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await LogAsync(fixture.Client, token, tank, new { amount = new { value = 20, unit = "liter" } });
        var earlier = DateTimeOffset.UtcNow.AddHours(-30);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { occurredAt = earlier });
        var entry = await BodyAsync(response);
        Assert.Equal(20m, entry.GetProperty("amount").GetProperty("value").GetDecimal());
        Assert.True(Math.Abs((entry.GetProperty("occurredAt").GetDateTimeOffset() - earlier).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task GivenNoteAndTime_WhenSentTogether_ThenBothApply()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await LogAsync(fixture.Client, token, tank, new { amount = new { value = 20, unit = "liter" } });
        var earlier = DateTimeOffset.UtcNow.AddHours(-5);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { occurredAt = earlier, note = "late" });
        var entry = await BodyAsync(response);
        Assert.Equal("late", entry.GetProperty("note").GetString());
        Assert.Equal(20m, entry.GetProperty("amount").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task GivenATimeInTheFuture_WhenCorrecting_ThenCodedProblemAndTheEntryStaysUnchanged()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await LogAsync(fixture.Client, token, tank, new { kind = "measurement", values = new[] { Nitrate(25m) } });
        using var before = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var occurred = (await BodyAsync(before)).GetProperty("items")[0].GetProperty("occurredAt").GetDateTimeOffset();
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { occurredAt = DateTimeOffset.UtcNow.AddHours(1) });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "care_time_invalid");
        using var after = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Equal(occurred, (await BodyAsync(after)).GetProperty("items")[0].GetProperty("occurredAt").GetDateTimeOffset());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(new[] { "care.logged" }, (await EventsAsync(db, "care", id)).Select(e => e.Type));
    }
}
