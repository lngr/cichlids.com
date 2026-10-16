using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareNoteTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenEntryWithAmount_WhenOnlyANoteIsSent_ThenNoteIsTrimmedAndAmountStays()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank, new { amount = new { value = 20, unit = "liter" } });
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = "  Added conditioner \n" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await BodyAsync(response);
        Assert.Equal("Added conditioner", entry.GetProperty("note").GetString());
        Assert.Equal(20m, entry.GetProperty("amount").GetProperty("value").GetDecimal());
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Equal("Added conditioner", (await BodyAsync(list)).GetProperty("items")[0].GetProperty("note").GetString());
    }

    [Fact]
    public async Task GivenNote_WhenChangedAndRemoved_ThenCareUpdatedEventsCarryTheNewNote()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var set = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = "first" });
        using var changed = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = "second" });
        using var removed = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = "   " });
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(removed)).GetProperty("note").ValueKind);
        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "care", id);
        Assert.Equal(new[] { "care.logged", "care.updated", "care.updated", "care.updated" }, events.Select(e => e.Type));
        Assert.Equal(new[] { "first", "second", null }, events.Skip(1).Select(e => e.Payload.GetProperty("note").GetString()));
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public async Task GivenNoteLength_WhenSaving_ThenTheLimitIs2000Characters(int length, bool accepted)
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        await using var db = fixture.CreateDbContext();
        var before = await db.OutboxEvents.CountAsync(e => e.AggregateType == "care" && e.AggregateId == id.ToString());
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = new string('n', length) });
        if (accepted) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "care_note_invalid");
            Assert.Equal(before, await db.OutboxEvents.CountAsync(e => e.AggregateType == "care" && e.AggregateId == id.ToString()));
        }
    }

    [Fact]
    public async Task GivenMeasurement_WhenNoteIsAdded_ThenValuesStayAndNoteIsListed()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank, new { kind = "measurement", values = new[] { new { parameter = "ph", value = 7.1 } } });
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { note = "after feeding" });
        var entry = await BodyAsync(response);
        Assert.Equal("after feeding", entry.GetProperty("note").GetString());
        Assert.Equal(7.1m, Assert.Single(entry.GetProperty("values").EnumerateArray()).GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task GivenAmountAndNote_WhenSentTogether_ThenBothApply()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token,
            new { amount = new { value = 30, unit = "percent" }, note = "big one" });
        var entry = await BodyAsync(response);
        Assert.Equal(30m, entry.GetProperty("amount").GetProperty("value").GetDecimal());
        Assert.Equal("big one", entry.GetProperty("note").GetString());
    }

    [Fact]
    public async Task GivenOtherMember_WhenWritingANote_ThenCodedNotFoundAndNoEvent()
    {
        var (owner, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, owner, tank);
        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", NewToken(), new { note = "mine now" });
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.Single(await EventsAsync(db, "care", id));
    }
}
