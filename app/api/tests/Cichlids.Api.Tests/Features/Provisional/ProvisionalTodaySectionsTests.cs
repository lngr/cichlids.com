using System.Net;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// A provisional caller opens the followed tanks and milestones sections of Today: it follows no tank,
/// and it dismisses milestones of its own tanks only.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.24")]
public sealed class ProvisionalTodaySectionsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenAProvisionalCaller_WhenOpeningTheFollowedSections_ThenTheyAreEmpty()
    {
        using var caller = await NewAsync(fixture.Client);

        using var today = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/today/followed", caller.Token);
        using var all = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/followed-tanks", caller.Token);

        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
        var body = await BodyAsync(today);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(0, (await BodyAsync(all)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenDismissingMilestones_ThenOnlyThoseOfItsOwnTanksAreStored()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        using var other = await NewAsync(fixture.Client);
        var foreign = await CreateTankAsync(fixture.Client, other);

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/today/milestones?date={DateTime.UtcNow:yyyy-MM-dd}&utcOffsetMinutes=0", caller.Token);
        using var own = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/today/milestones/kept_streak:{tank}:4/dismissal", caller.Token);
        using var foreignDismissal = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/me/today/milestones/kept_streak:{foreign}:4/dismissal", caller.Token);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDismissal.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.MilestoneDismissals.CountAsync(d => d.ProfileId == caller.ProfileId));
    }
}
