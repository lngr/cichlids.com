using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// The read paths of a tank for the three viewers: the owner, another member and a caller without a
/// login. A tank or stream the viewer may not see answers a coded 404, exactly as a missing one.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TankVisibilityReadPathTests(ApiFixture fixture) : IAsyncLifetime
{
    private TankVisibilityWorld world = null!;

    public async Task InitializeAsync() => world = await TankVisibilityWorld.CreateAsync(fixture);

    public Task DisposeAsync() => world.DisposeAsync(fixture);

    public enum Viewer { Owner, Other, Anonymous }

    private string? TokenOf(Viewer viewer) => viewer switch
    {
        Viewer.Owner => world.Owner.Token,
        Viewer.Other => world.Other.Token,
        _ => null,
    };

    private Task<HttpResponseMessage> GetAsync(Viewer viewer, string path) => SendAsync(fixture, HttpMethod.Get, path, TokenOf(viewer));

    private long TankOf(string which) => which switch
    {
        "open" => world.OpenTank,
        "stream_private" => world.StreamPrivateTank,
        _ => world.PrivateTank,
    };

    public static TheoryData<Viewer, string, bool, bool> Matrix => new()
    {
        // viewer, tank, profile visible, stream visible
        { Viewer.Owner, "open", true, true },
        { Viewer.Owner, "stream_private", true, true },
        { Viewer.Owner, "private", true, true },
        { Viewer.Other, "open", true, true },
        { Viewer.Other, "stream_private", true, false },
        { Viewer.Other, "private", false, false },
        { Viewer.Anonymous, "open", true, true },
        { Viewer.Anonymous, "stream_private", true, false },
        { Viewer.Anonymous, "private", false, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task TankDetail_FollowsTheProfileLevel(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = streamVisible;
        using var response = await GetAsync(viewer, $"/api/tanks/{TankOf(which)}");
        if (profileVisible)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var visibility = (await BodyAsync(response)).GetProperty("visibility");
            Assert.Equal(which == "private" ? "private" : "public", visibility.GetProperty("profile").GetString());
            Assert.Equal(which == "open" ? "public" : "private", visibility.GetProperty("stream").GetString());
        }
        else
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task TankTimeLapse_FollowsTheProfileLevel(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = streamVisible;
        using var response = await GetAsync(viewer, $"/api/tanks/{TankOf(which)}/time-lapse");
        if (profileVisible) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task TankVideos_FollowTheProfileLevel(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = streamVisible;
        using var response = await GetAsync(viewer, $"/api/tanks/{TankOf(which)}/videos");
        if (profileVisible) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task TankComments_FollowTheProfileLevel(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = streamVisible;
        using var response = await GetAsync(viewer, $"/api/tanks/{TankOf(which)}/comments");
        if (profileVisible) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("stream_private", true)]
    [InlineData("private", false)]
    public async Task CommentingOnATank_NeedsAVisibleProfile(string which, bool allowed)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{TankOf(which)}/comments", world.Other.Token,
            new { body = "Nice tank", stars = 5 });
        if (allowed) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CareEntries_ShowTheStreamAndNeverTheEntriesMarkedOnlyMe(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = profileVisible;
        var tank = TankOf(which);
        var entries = world.Entries[tank];
        using var response = await GetAsync(viewer, $"/api/tanks/{tank}/care");
        if (!streamVisible)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(body.GetProperty("total").GetInt32(), ids.Count);
        if (viewer == Viewer.Owner)
        {
            Assert.Equal(
                new[] { entries.PublicWaterChange, entries.PublicMeasurement, entries.OnlyMeWaterChange, entries.OnlyMeMeasurement }.Order(), ids.Order());
        }
        else
        {
            Assert.Equal(new[] { entries.PublicWaterChange, entries.PublicMeasurement }.Order(), ids.Order());
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CareSeries_ShowTheStreamAndNeverTheMeasurementsMarkedOnlyMe(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = profileVisible;
        var tank = TankOf(which);
        using var response = await GetAsync(viewer, $"/api/tanks/{tank}/care/series?parameter=ph");
        if (!streamVisible)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = (await BodyAsync(response)).GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()).ToList();
        Assert.Equal(viewer == Viewer.Owner ? [7.0m, 8.5m] : [7.0m], values.Order());
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task AllCareSeries_ShowTheStreamAndNeverTheMeasurementsMarkedOnlyMe(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = profileVisible;
        var tank = TankOf(which);
        using var response = await GetAsync(viewer, $"/api/tanks/{tank}/care/series/all");
        if (!streamVisible)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var series = (await BodyAsync(response)).GetProperty("series").EnumerateArray()
            .Single(s => s.GetProperty("parameter").GetString() == "ph");
        var values = series.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()).ToList();
        Assert.Equal(viewer == Viewer.Owner ? [7.0m, 8.5m] : [7.0m], values.Order());
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CarePlan_ShowsTheStreamAndIgnoresEntriesMarkedOnlyMe(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = profileVisible;
        var tank = TankOf(which);
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        using var response = await GetAsync(viewer, $"/api/tanks/{tank}/care/plan?date={today}");
        if (!streamVisible)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rule = (await BodyAsync(response)).GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("kind").GetString() == "water_change");
        var due = rule.GetProperty("due");
        // The public water change is ten days old on a seven day rhythm; the one marked only me is a day old.
        Assert.Equal(viewer == Viewer.Owner ? "due_in" : "overdue", due.GetProperty("state").GetString());
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task CareEntries_HideTheToDosOfTheOwnerFromEveryoneElse(Viewer viewer, string which, bool profileVisible, bool streamVisible)
    {
        _ = profileVisible;
        if (!streamVisible) return;
        var tank = TankOf(which);
        using var response = await GetAsync(viewer, $"/api/tanks/{tank}/care");
        var entry = (await BodyAsync(response)).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetInt64() == world.Entries[tank].PublicWaterChange);
        Assert.Equal(viewer == Viewer.Owner ? 1 : 0, entry.GetProperty("todos").GetArrayLength());
    }

    [Theory]
    [InlineData(Viewer.Other)]
    [InlineData(Viewer.Anonymous)]
    public async Task ACaller_CannotWriteToTheCareOfATankThatIsNotTheirs(Viewer viewer)
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/tanks/{world.OpenTank}/care", TokenOf(viewer), new { amount = new { value = 5, unit = "liter" } });
        Assert.Equal(viewer == Viewer.Anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(Viewer.Owner, 3)]
    [InlineData(Viewer.Other, 2)]
    [InlineData(Viewer.Anonymous, 2)]
    public async Task TheTankList_ShowsOnlyTheTanksTheViewerSees(Viewer viewer, int expected)
    {
        using var response = await GetAsync(viewer, $"/api/tanks?user={world.Owner.ProfileId}");
        var items = (await BodyAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(expected, items.Count);
        Assert.Contains(world.OpenTank, items);
        Assert.Equal(viewer == Viewer.Owner, items.Contains(world.PrivateTank));
    }

    [Theory]
    [InlineData(Viewer.Owner, 3)]
    [InlineData(Viewer.Other, 2)]
    [InlineData(Viewer.Anonymous, 2)]
    public async Task TheProfileTankList_ShowsOnlyTheTanksTheViewerSees(Viewer viewer, int expected)
    {
        using var response = await GetAsync(viewer, $"/api/profiles/{world.Owner.ProfileId}/tanks");
        var items = (await BodyAsync(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(expected, items.Count);
        Assert.Equal(viewer == Viewer.Owner, items.Contains(world.PrivateTank));
    }

    [Theory]
    [InlineData(Viewer.Owner, 3)]
    [InlineData(Viewer.Other, 2)]
    [InlineData(Viewer.Anonymous, 2)]
    public async Task TheProfileTankCount_CountsOnlyTheTanksTheViewerSees(Viewer viewer, int expected)
    {
        using var response = await GetAsync(viewer, $"/api/profiles/{world.Owner.ProfileId}");
        Assert.Equal(expected, (await BodyAsync(response)).GetProperty("stats").GetProperty("tankCount").GetInt32());
    }
}
