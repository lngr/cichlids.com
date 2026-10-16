using System.Net;
using System.Text.Json;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Streaks.StreakTestSupport;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Streaks;

[Collection(ApiCollection.Name)]
public sealed class VacationEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private static string Url(string path = "", string date = Today) => $"/api/me/vacations{path}?date={date}";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => RemoveEventsAsync(fixture, "vacation", "streak_freeze");

    private async Task<List<(string Type, JsonElement Payload)>> EventsAsync(long profile)
    {
        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "vacation").OrderBy(e => e.OccurredAt).ThenByDescending(e => e.EventType).ToListAsync();
        return events.Select(e => (e.EventType, JsonDocument.Parse(e.Payload).RootElement.Clone()))
            .Where(e => e.Item2.GetProperty("profileId").GetInt64() == profile).ToList();
    }

    [Fact]
    public async Task GivenNoVacation_WhenStartingOneNowAndEndingIt_ThenItIsActiveThenPastAndTheEventsFollow()
    {
        var member = await NewMemberAsync(fixture);
        using var started = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var vacation = await BodyAsync(started);
        Assert.Equal("2026-10-15", vacation.GetProperty("startDate").GetString());
        Assert.Equal(JsonValueKind.Null, vacation.GetProperty("endDate").ValueKind);
        Assert.True(vacation.GetProperty("active").GetBoolean());

        using var again = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { });
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "vacation_active");

        using var ended = await SendAsync(fixture, HttpMethod.Post, Url("/active/end", "2026-10-20"), member.Token);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        var past = await BodyAsync(ended);
        Assert.Equal("2026-10-20", past.GetProperty("endDate").GetString());
        Assert.False(past.GetProperty("active").GetBoolean());

        var events = await EventsAsync(member.ProfileId);
        Assert.Equal(["vacation.started", "vacation.ended"], events.Select(e => e.Type));
        Assert.Equal("2026-10-15", events[0].Payload.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-20", events[1].Payload.GetProperty("endDate").GetString());

        using var list = await SendAsync(fixture, HttpMethod.Get, "/api/me/vacations", member.Token);
        Assert.Single((await BodyAsync(list)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GivenNoActiveVacation_WhenEnding_ThenTheVacationIsMissing()
    {
        var member = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Post, Url("/active/end"), member.Token);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "vacation_not_found");
    }

    [Fact]
    public async Task GivenAStartAfterTheEndDate_WhenEnding_ThenTheDatesAreRefused()
    {
        var member = await NewMemberAsync(fixture);
        using var started = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = "2026-10-10" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var response = await SendAsync(fixture, HttpMethod.Post, Url("/active/end", "2026-10-09"), member.Token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "vacation_dates_invalid");
    }

    [Fact]
    public async Task GivenAPastVacation_WhenRecordingItAfterwards_ThenItIsStoredAsEndedWithBothEvents()
    {
        var member = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = "2026-09-01", endDate = "2026-09-14" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vacation = await BodyAsync(response);
        Assert.False(vacation.GetProperty("active").GetBoolean());
        Assert.Equal("2026-09-14", vacation.GetProperty("endDate").GetString());
        Assert.Equal(["vacation.started", "vacation.ended"], (await EventsAsync(member.ProfileId)).Select(e => e.Type));
    }

    [Theory]
    [InlineData("2026-10-16", null)]
    [InlineData("2026-09-01", "2026-10-16")]
    [InlineData("2026-09-14", "2026-09-01")]
    [InlineData(null, "2026-09-01")]
    public async Task GivenImpossibleDates_WhenStartingOrRecording_ThenTheDatesAreRefused(string? start, string? end)
    {
        var member = await NewMemberAsync(fixture);
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = start, endDate = end });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "vacation_dates_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Vacations.AnyAsync(v => v.ProfileId == member.ProfileId));
    }

    [Theory]
    [InlineData("2026-09-10", "2026-09-20")]
    [InlineData("2026-09-14", "2026-09-30")]
    [InlineData("2026-08-20", "2026-09-01")]
    [InlineData("2026-09-05", "2026-09-06")]
    public async Task GivenAnExistingVacation_WhenRecordingAnOverlappingOne_ThenItIsRefused(string start, string end)
    {
        var member = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));
        using var response = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = start, endDate = end });
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vacation_overlap");
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenRecordingAPastOneBeforeIt_ThenItIsStoredAndOneThatOverlapsIsRefused()
    {
        var member = await NewMemberAsync(fixture);
        await AddVacationAsync(fixture, member.ProfileId, new DateOnly(2026, 10, 10), null);
        using var before = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = "2026-09-01", endDate = "2026-09-09" });
        Assert.Equal(HttpStatusCode.Created, before.StatusCode);
        using var overlapping = await SendAsync(fixture, HttpMethod.Post, Url(), member.Token, new { startDate = "2026-10-01", endDate = "2026-10-12" });
        await AssertProblemAsync(overlapping, HttpStatusCode.Conflict, "vacation_overlap");
    }

    [Fact]
    public async Task GivenNoLoginOrADate_WhenCallingTheVacationRoutes_ThenTheProblemsHaveCodes()
    {
        using var anonymous = await SendAsync(fixture, HttpMethod.Post, Url(), null, new { });
        await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "streak_auth_required");
        var member = await NewMemberAsync(fixture);
        using var noDate = await SendAsync(fixture, HttpMethod.Post, "/api/me/vacations", member.Token, new { });
        await AssertProblemAsync(noDate, HttpStatusCode.BadRequest, "streak_date_invalid");
        using var list = await SendAsync(fixture, HttpMethod.Get, "/api/me/vacations", null);
        await AssertProblemAsync(list, HttpStatusCode.Unauthorized, "streak_auth_required");
    }

    [Fact]
    public async Task GivenPastAndActiveVacations_WhenOthersReadTheProfile_ThenOnlyThePastShareIsShownWhereATankIsVisible()
    {
        var member = await NewMemberAsync(fixture);
        var viewer = await NewMemberAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await AddVacationAsync(fixture, member.ProfileId, today.AddDays(-30), today.AddDays(-21));
        await AddVacationAsync(fixture, member.ProfileId, today.AddDays(-2), null);

        var hidden = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}", viewer.Token));
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("vacationShare").ValueKind);

        await NewTankAsync(fixture, member.ProfileId, TankVisibility.Private, TankVisibility.Private);
        var stillHidden = await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}", viewer.Token));
        Assert.Equal(JsonValueKind.Null, stillHidden.GetProperty("vacationShare").ValueKind);

        await NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var seen = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}", viewer.Token))).GetProperty("vacationShare");
        Assert.Equal(10, seen.GetProperty("vacationDays").GetInt32());
        Assert.Equal(1, seen.GetProperty("pastVacationCount").GetInt32());
        Assert.True(seen.GetProperty("windowDays").GetInt32() >= 1);

        var own = (await BodyAsync(await SendAsync(fixture, HttpMethod.Get, $"/api/profiles/{member.ProfileId}", member.Token))).GetProperty("vacationShare");
        Assert.Equal(10, own.GetProperty("vacationDays").GetInt32());
    }
}
