using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareHeatmapEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Today = "2026-10-15";
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_tanks.Count > 0) await RemoveTanksAsync(fixture, _tanks.ToArray());
    }

    private static string TankUrl(long tank, string date = Today, int offset = 0) =>
        $"/api/tanks/{tank}/care/heatmap?date={date}&utcOffsetMinutes={offset}";

    private static string OwnUrl(string date = Today, int offset = 0) => $"/api/me/care/heatmap?date={date}&utcOffsetMinutes={offset}";

    private static DateTimeOffset At(int month, int day, int hour = 12, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private async Task<long> TankAsync(long owner, TankVisibility profile = TankVisibility.Public, TankVisibility stream = TankVisibility.Public)
    {
        var tank = await NewTankAsync(fixture, owner, profile, stream);
        _tanks.Add(tank);
        return tank;
    }

    private async Task AddAsync(long tank, long profile, CareKind kind, DateTimeOffset at, bool onlyMe = false)
    {
        await using var db = fixture.CreateDbContext();
        db.CareEntries.Add(new CareEntry { TankId = tank, AuthorProfileId = profile, Kind = kind, OccurredAt = at, RecordedAt = at, OnlyMe = onlyMe });
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, (int Water, int Measure)> Days(JsonElement body) =>
        body.GetProperty("days").EnumerateArray().ToDictionary(d => d.GetProperty("date").GetString()!,
            d => (d.GetProperty("waterChanges").GetInt32(), d.GetProperty("measurements").GetInt32()));

    [Fact]
    public async Task GivenCareOnSeveralDays_WhenTheOwnerReadsTheHeatmap_ThenExactlyThoseDaysCarryCounts()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 1));
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 1, 18));
        await AddAsync(tank, owner.ProfileId, CareKind.Measurement, At(10, 1, 9));
        await AddAsync(tank, owner.ProfileId, CareKind.Measurement, At(10, 8));
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 15));
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(6, 28));
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 16));

        using var response = await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), owner.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("2026-06-29", body.GetProperty("start").GetString());
        Assert.Equal(Today, body.GetProperty("end").GetString());
        Assert.Equal(new Dictionary<string, (int, int)>
        {
            ["2026-10-01"] = (2, 1), ["2026-10-08"] = (0, 1), ["2026-10-15"] = (1, 0),
        }, Days(body));
        Assert.Equal(3, body.GetProperty("activeDays").GetInt32());
        Assert.Equal("2026-10-15", body.GetProperty("lastWaterChange").GetString());
        Assert.Equal("2026-10-08", body.GetProperty("lastMeasurement").GetString());
    }

    [Fact]
    public async Task GivenAnEntryNearMidnightUtc_WhenTheOffsetChanges_ThenTheLocalDayChanges()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 14, 23, 30));

        var east = Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank, Today, 120), owner.Token)));
        Assert.Equal(["2026-10-15"], east.Keys);
        var utc = Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank, Today, 0), owner.Token)));
        Assert.Equal(["2026-10-14"], utc.Keys);
        var west = Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank, "2026-10-14", -300), owner.Token)));
        Assert.Equal(["2026-10-14"], west.Keys);
    }

    [Fact]
    public async Task GivenAnEntryAtTheEdgeOfTheWindow_WhenTheOffsetMovesTheLocalDay_ThenItIsInOrOut()
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(6, 28, 23, 0));
        Assert.Empty(Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), owner.Token))));
        var east = Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank, Today, 120), owner.Token)));
        Assert.Equal(["2026-06-29"], east.Keys);
    }

    [Fact]
    public async Task GivenOnlyMeEntries_WhenOthersReadTheHeatmap_ThenTheyNeverSeeThem()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 5));
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 6), onlyMe: true);
        await AddAsync(tank, owner.ProfileId, CareKind.Measurement, At(10, 6), onlyMe: true);

        Assert.Equal(["2026-10-05", "2026-10-06"], Days(await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), owner.Token))).Keys.Order());
        foreach (var token in new string?[] { viewer.Token, null })
        {
            var body = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), token));
            Assert.Equal(["2026-10-05"], Days(body).Keys);
            Assert.Equal(1, body.GetProperty("activeDays").GetInt32());
        }
    }

    [Theory]
    [InlineData(TankVisibility.Public, TankVisibility.Private)]
    [InlineData(TankVisibility.Private, TankVisibility.Private)]
    public async Task GivenAPrivateStream_WhenOthersReadTheHeatmap_ThenTheAnswerIsTheCodedNotFoundOfAMissingTank(
        TankVisibility profile, TankVisibility stream)
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId, profile, stream);
        await AddAsync(tank, owner.ProfileId, CareKind.WaterChange, At(10, 5));

        foreach (var token in new string?[] { viewer.Token, null })
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
        }
        using var missing = await SendAsync(fixture, HttpMethod.Get, TankUrl(long.MaxValue), viewer.Token);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "care_not_found");
        using var own = await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), owner.Token);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task GivenAPublicTank_WhenAMemberOrAnAnonymousVisitorReads_ThenTheyGetTheHeatmap()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        await AddAsync(tank, owner.ProfileId, CareKind.Measurement, At(10, 5));
        foreach (var token in new string?[] { viewer.Token, null })
        {
            using var response = await SendAsync(fixture, HttpMethod.Get, TankUrl(tank), token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal((0, 1), Days(await BodyAsync(response))["2026-10-05"]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("&date=2026-13-01")]
    public async Task GivenAnInvalidDate_WhenTheHeatmapIsRead_ThenTheCodedBadRequestAnswers(string suffix)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await TankAsync(owner.ProfileId);
        using var tankResponse = await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/care/heatmap?x=1{suffix}", owner.Token);
        await AssertProblemAsync(tankResponse, HttpStatusCode.BadRequest, "care_date_invalid");
        using var ownResponse = await SendAsync(fixture, HttpMethod.Get, $"/api/me/care/heatmap?x=1{suffix}", owner.Token);
        await AssertProblemAsync(ownResponse, HttpStatusCode.BadRequest, "care_date_invalid");
    }

    [Fact]
    public async Task GivenSeveralTanks_WhenTheMemberReadsTheirHeatmap_ThenTheOwnTanksAreSummedAndOthersAreExcluded()
    {
        var owner = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        var first = await TankAsync(owner.ProfileId, TankVisibility.Private, TankVisibility.Private);
        var second = await TankAsync(owner.ProfileId);
        var foreign = await TankAsync(other.ProfileId);
        await AddAsync(first, owner.ProfileId, CareKind.WaterChange, At(10, 5));
        await AddAsync(second, owner.ProfileId, CareKind.WaterChange, At(10, 5));
        await AddAsync(second, owner.ProfileId, CareKind.Measurement, At(10, 7), onlyMe: true);
        await AddAsync(foreign, other.ProfileId, CareKind.WaterChange, At(10, 5));
        await AddAsync(foreign, other.ProfileId, CareKind.WaterChange, At(10, 9));

        using var response = await SendAsync(fixture, HttpMethod.Get, OwnUrl(), owner.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(new Dictionary<string, (int, int)> { ["2026-10-05"] = (2, 0), ["2026-10-07"] = (0, 1) }, Days(body));
        Assert.Equal(2, body.GetProperty("activeDays").GetInt32());
    }

    [Fact]
    public async Task GivenNoLogin_WhenTheMemberHeatmapIsRead_ThenTheCodedUnauthorizedAnswers()
    {
        using var response = await SendAsync(fixture, HttpMethod.Get, OwnUrl());
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "care_auth_required");
    }
}
