using System.Net;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareOnlyMeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenAnEntryLoggedOnlyMe_WhenAnotherMemberReads_ThenOnlyTheOwnerSeesIt()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var other = NewMemberToken();
        using var logged = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/care", token,
            new { amount = new { value = 20, unit = "liter" }, onlyMe = true });
        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var secret = await BodyAsync(logged);
        Assert.True(secret.GetProperty("onlyMe").GetBoolean());
        var publicId = await LogAsync(fixture.Client, token, tank, new { amount = new { value = 10, unit = "liter" } });

        using var ownList = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        using var otherList = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", other);
        using var anonymousList = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", null);

        Assert.Equal(2, (await BodyAsync(ownList)).GetProperty("total").GetInt32());
        foreach (var list in new[] { otherList, anonymousList })
        {
            var body = await BodyAsync(list);
            Assert.Equal(1, body.GetProperty("total").GetInt32());
            Assert.Equal(publicId, body.GetProperty("items")[0].GetProperty("id").GetInt64());
            Assert.False(body.GetProperty("items")[0].GetProperty("onlyMe").GetBoolean());
        }

        await using var db = fixture.CreateDbContext();
        Assert.True((await db.CareEntries.AsNoTracking().SingleAsync(e => e.Id == secret.GetProperty("id").GetInt64())).OnlyMe);
        var events = await EventsAsync(db, "care", secret.GetProperty("id").GetInt64());
        Assert.True(Assert.Single(events).Payload.GetProperty("onlyMe").GetBoolean());
        await RemoveTanksAsync(db, [tank]);
    }

    [Fact]
    public async Task GivenAnEntryWithoutTheMark_WhenLogged_ThenItIsVisibleToTheStream()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);
        await using var db = fixture.CreateDbContext();
        Assert.False((await db.CareEntries.AsNoTracking().SingleAsync(e => e.Id == id)).OnlyMe);
        await RemoveTanksAsync(db, [tank]);
    }

    [Fact]
    public async Task GivenAnEntry_WhenTheOwnerMarksItOnlyMeAndBack_ThenOnlyTheMarkChangesAndEachChangeIsAnEvent()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank, new { amount = new { value = 15, unit = "liter" } });

        using var marked = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { onlyMe = true });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        var entry = await BodyAsync(marked);
        Assert.True(entry.GetProperty("onlyMe").GetBoolean());
        Assert.Equal(15, entry.GetProperty("amount").GetProperty("value").GetDecimal());

        using var hidden = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", NewMemberToken());
        Assert.Equal(0, (await BodyAsync(hidden)).GetProperty("total").GetInt32());

        using var shown = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", token, new { onlyMe = false, note = "Visible again" });
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        var shownEntry = await BodyAsync(shown);
        Assert.False(shownEntry.GetProperty("onlyMe").GetBoolean());
        Assert.Equal("Visible again", shownEntry.GetProperty("note").GetString());

        using var visible = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", NewMemberToken());
        Assert.Equal(1, (await BodyAsync(visible)).GetProperty("total").GetInt32());

        await using var db = fixture.CreateDbContext();
        var updates = (await EventsAsync(db, "care", id)).Where(e => e.Type == "care.updated").ToList();
        Assert.Equal([true, false], updates.Select(e => e.Payload.GetProperty("onlyMe").GetBoolean()));
        await RemoveTanksAsync(db, [tank]);
    }

    [Fact]
    public async Task GivenAnotherMember_WhenMarkingAnEntryOnlyMe_ThenTheEntryIsNotFound()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = await LogAsync(fixture.Client, token, tank);

        using var response = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{id}", NewMemberToken(), new { onlyMe = true });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.False((await db.CareEntries.AsNoTracking().SingleAsync(e => e.Id == id)).OnlyMe);
        await RemoveTanksAsync(db, [tank]);
    }
}
