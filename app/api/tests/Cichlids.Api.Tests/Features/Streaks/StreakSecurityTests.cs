using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Streaks;

/// <summary>
/// Input validation, repair refusal and disclosure rules of the streak and vacation routes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StreakSecurityTests(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => RemoveEventsAsync(fixture, "streak_freeze", "vacation");

    private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd");

    [Fact]
    public async Task GivenADateFarFromTheServerDate_WhenCallingAnyStreakRoute_ThenTheDateIsRefused()
    {
        using var strict = fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Streaks:MaxClientDateSkewDays"] = "1" }))).CreateClient();
        var member = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var today = DateTime.UtcNow;
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, $"/api/tanks/{tank}/streaks?date={Iso(today.AddDays(30))}"),
            (HttpMethod.Get, $"/api/me/streaks?date={Iso(today.AddDays(-30))}"),
            (HttpMethod.Post, $"/api/me/vacations?date={Iso(today.AddDays(5))}"),
            (HttpMethod.Post, $"/api/me/vacations/active/end?date={Iso(today.AddDays(-5))}"),
        })
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", member.Token);
            using var response = await strict.SendAsync(request);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "streak_date_invalid");
        }
        using var ok = new HttpRequestMessage(HttpMethod.Get, $"/api/tanks/{tank}/streaks?date={Iso(today)}");
        using var accepted = await strict.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task GivenAVacationLongerThanAYear_WhenRecordingIt_ThenItIsRefused()
    {
        var member = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations?date={Today}", member.Token,
            new { startDate = "2025-01-01", endDate = "2026-01-05" });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "vacation_dates_invalid");
        using var fine = await SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations?date={Today}", member.Token,
            new { startDate = "2025-01-01", endDate = "2025-12-31" });
        Assert.Equal(HttpStatusCode.Created, fine.StatusCode);
    }

    private async Task<(Member Member, long Tank)> BrokenPeriodAsync()
    {
        var member = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(9, 1), true);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(9, 8), true);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(10, 12), true);
        return (member, tank);
    }

    [Fact]
    public async Task GivenABrokenPeriod_WhenRecordingAVacationAfterwardsThatWouldRepairIt_ThenItIsRefusedAndNothingIsStored()
    {
        var (member, _) = await BrokenPeriodAsync();
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations?date=2026-10-15", member.Token,
            new { startDate = "2026-09-10", endDate = "2026-10-09" });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vacation_would_repair");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Vacations.AnyAsync(v => v.ProfileId == member.ProfileId));
    }

    [Fact]
    public async Task GivenABrokenTimeLapseRun_WhenRecordingAVacationThatWouldBridgeIt_ThenItIsRefused()
    {
        var member = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddSeriesPhotoAsync(fixture, tank, member.ProfileId, At(8, 31));
        await AddSeriesPhotoAsync(fixture, tank, member.ProfileId, At(10, 13));
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", member.Token,
            new { startDate = "2026-09-07", endDate = "2026-10-11" });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vacation_would_repair");
    }

    [Fact]
    public async Task GivenARecentStart_WhenTheVacationWouldPauseAPeriodThatJustRanOut_ThenItIsAccepted()
    {
        var member = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(10, 1), true);
        await AddWaterChangeAsync(fixture, tank, member.ProfileId, At(10, 8), true);
        using var old = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-20", member.Token, new { startDate = "2026-10-09", endDate = "2026-10-19" });
        await AssertProblemAsync(old, HttpStatusCode.Conflict, "vacation_would_repair");
        using var recent = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-20", member.Token, new { startDate = "2026-10-15" });
        Assert.Equal(HttpStatusCode.Created, recent.StatusCode);
    }

    [Fact]
    public async Task GivenAVacationThatRepairsNothing_WhenRecordingItAfterwards_ThenItIsAccepted()
    {
        var (member, _) = await BrokenPeriodAsync();
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", member.Token,
            new { startDate = "2026-08-01", endDate = "2026-08-10" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenOthersRead_ThenNoLastActivityDateIsExposed()
    {
        var owner = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddWaterChangeAsync(fixture, tank, owner.ProfileId, At(10, 1), true);
        await AddVacationAsync(fixture, owner.ProfileId, new DateOnly(2026, 10, 10), null);
        var seen = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/streaks?date={Today}", viewer.Token))).GetProperty("waterChange");
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("lastWaterChange").ValueKind);
        var own = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/tanks/{tank}/streaks?date={Today}", owner.Token))).GetProperty("waterChange");
        Assert.Equal("2026-10-01", own.GetProperty("lastWaterChange").GetString());
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenOthersReadTheProfile_ThenTheShareWindowDoesNotRevealItsStart()
    {
        var member = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        await AddVacationAsync(fixture, member.ProfileId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30), null);
        var share = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}", viewer.Token))).GetProperty("vacationShare");
        Assert.Equal(0, share.GetProperty("vacationDays").GetInt32());
        Assert.Equal(1, share.GetProperty("windowDays").GetInt32());
    }

    [Fact]
    public async Task GivenAnotherMembersVacation_WhenEndingOrListing_ThenOnlyOwnVacationsAreTouched()
    {
        var owner = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, owner.ProfileId, new DateOnly(2026, 10, 10), null);
        using var ended = await SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations/active/end?date={Today}", other.Token);
        await AssertProblemAsync(ended, HttpStatusCode.NotFound, "vacation_not_found");
        var list = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, "/api/me/vacations", other.Token));
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Vacations.AnyAsync(v => v.ProfileId == owner.ProfileId && v.EndDate == null));
    }

    [Fact]
    public async Task GivenOnlyTheOwnerOfATank_WhenSomeoneElseUploadsASeriesPhoto_ThenItIsRefused()
    {
        var owner = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        using var content = new MultipartFormDataContent { { new StringContent("true"), "capturedInApp" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tanks/{tank}/time-lapse/photos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", other.Token);
        using var response = await fixture.Client.SendAsync(request);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.TankMedia.AnyAsync(m => m.TankId == tank));
    }

    [Fact]
    public async Task GivenNoLoginAndInvalidDates_WhenStartingAVacation_ThenTheLoginIsCheckedFirst()
    {
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=not-a-date", null, new { startDate = "x" });
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "streak_auth_required");
    }

    [Fact]
    public async Task GivenInvalidDatesAndAnOverlap_WhenRecording_ThenTheDatesAreRefusedBeforeTheOverlap()
    {
        var member = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));
        using var response = await SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations?date={Today}", member.Token, new { startDate = "2026-09-10", endDate = "2026-09-05" });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "vacation_dates_invalid");
    }

    [Fact]
    public async Task GivenAnOverlapThatWouldAlsoRepair_WhenRecording_ThenTheOverlapIsReportedBeforeTheRepairRule()
    {
        var (member, _) = await BrokenPeriodAsync();
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 25));
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", member.Token, new { startDate = "2026-09-10", endDate = "2026-10-09" });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vacation_overlap");
    }

    [Fact]
    public async Task GivenAnActiveVacationAndARepair_WhenStartingAnother_ThenTheActiveVacationIsReportedFirst()
    {
        var (member, _) = await BrokenPeriodAsync();
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 10, 14), null);
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", member.Token, new { startDate = "2026-09-10" });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vacation_active");
    }

    [Fact]
    public async Task GivenTwoSimultaneousStarts_WhenBothArrive_ThenExactlyOneVacationIsActive()
    {
        var member = await NewMemberAsync(fixture);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            SendAsync(fixture, HttpMethod.Post, $"/api/me/vacations?date={Today}", member.Token, new { })));
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var r in responses) r.Dispose(); }
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Vacations.CountAsync(v => v.ProfileId == member.ProfileId));
    }

    [Fact]
    public async Task GivenTwoSimultaneousOverlappingRecords_WhenBothArrive_ThenOnlyOneIsStored()
    {
        var member = await NewMemberAsync(fixture);
        var bodies = new[] { new { startDate = "2026-09-01", endDate = "2026-09-10" }, new { startDate = "2026-09-05", endDate = "2026-09-15" },
            new { startDate = "2026-09-08", endDate = "2026-09-12" }, new { startDate = "2026-09-09", endDate = "2026-09-20" } };
        var responses = await Task.WhenAll(bodies.Select(b => SendAsync(fixture, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", member.Token, b)));
        try { Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created)); }
        finally { foreach (var r in responses) r.Dispose(); }
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Vacations.CountAsync(v => v.ProfileId == member.ProfileId));
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenEndingBeforeItsStart_ThenItIsRefusedAndStaysUnchanged()
    {
        var member = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 10, 10), null);
        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations/active/end?date=2026-10-09", member.Token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "vacation_dates_invalid");
        using var ended = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations/active/end?date=2026-10-12", member.Token, new { startDate = "2026-01-01" });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Vacations.SingleAsync(v => v.ProfileId == member.ProfileId);
        Assert.Equal(new DateOnly(2026, 10, 10), stored.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 12), stored.EndDate);
    }

    [Fact]
    public async Task GivenTwoSimultaneousEnds_WhenBothArrive_ThenOneEndsAndTheOtherFindsNoActiveVacation()
    {
        var member = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 10, 10), null);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            SendAsync(fixture, HttpMethod.Post, "/api/me/vacations/active/end?date=2026-10-12", member.Token)));
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.NotFound));
        }
        finally { foreach (var r in responses) r.Dispose(); }
    }
}
