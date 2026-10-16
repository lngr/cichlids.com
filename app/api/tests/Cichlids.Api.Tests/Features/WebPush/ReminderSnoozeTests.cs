using System.Net;
using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// Snoozing a care reminder from a notification: the server stores the snooze for the rule and its
/// current due date, the reminder returns at 09:00 on the next local day of the browser, and only
/// the owner of the tank can snooze.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.12")]
public sealed class ReminderSnoozeTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Snooze = "/api/me/care/reminders/snooze";
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.OutboxEvents.Where(e => e.AggregateType == "care_reminder").ExecuteDeleteAsync();
        await RemoveTanksAsync(db, _tanks);
    }

    /// <summary>A tank whose water change falls due on the UTC date of today.</summary>
    private async Task<(string Token, long Tank, string DueDate)> TankDueTodayAsync()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        _tanks.Add(tank);
        var lastWaterChange = DateTimeOffset.UtcNow.AddDays(-CarePlans.DefaultWaterChangeRhythmDays);
        await LogAsync(fixture.Client, token, tank, new { kind = "water_change", occurredAt = lastWaterChange });
        return (token, tank, DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task GivenADueReminder_WhenSnoozing_ThenTheSnoozeIsStoredUntilTomorrowOfTheBrowserAndOneEventIsRecorded()
    {
        var (token, tank, dueDate) = await TankDueTodayAsync();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, token,
            new { tankId = tank, kind = "water_change", dueDate, timeZone = "Etc/UTC" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(until, (await BodyAsync(response)).GetProperty("untilDate").GetString());
        await using var db = fixture.CreateDbContext();
        var row = await db.CareReminderSnoozes.AsNoTracking().SingleAsync(s => s.TankId == tank);
        Assert.Equal(CareKind.WaterChange, row.Kind);
        Assert.Equal(DateOnly.Parse(dueDate), row.DueDate);
        Assert.Equal(DateOnly.Parse(until), row.UntilDate);
        var snoozed = Assert.Single((await EventsAsync(db, "care_reminder", row.Id)));
        Assert.Equal("care_reminder.snoozed", snoozed.Type);
        Assert.Equal(tank, snoozed.Payload.GetProperty("tankId").GetInt64());
        Assert.Equal(until, snoozed.Payload.GetProperty("untilDate").GetString());
    }

    [Fact]
    public async Task GivenASnooze_WhenSnoozingAgain_ThenTheSameRowMovesAndNoSecondRowExists()
    {
        var (token, tank, dueDate) = await TankDueTodayAsync();
        var body = new { tankId = tank, kind = "water_change", dueDate, timeZone = "Etc/UTC" };
        using var first = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, token, body);
        using var second = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, token, body);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.CareReminderSnoozes.CountAsync(s => s.TankId == tank));
    }

    [Fact]
    public async Task GivenAnotherMembersTank_WhenSnoozing_ThenCodedNotFoundAndNothingIsStored()
    {
        var (_, tank, dueDate) = await TankDueTodayAsync();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, NewMemberToken(),
            new { tankId = tank, kind = "water_change", dueDate, timeZone = "Etc/UTC" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "care_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.CareReminderSnoozes.AnyAsync(s => s.TankId == tank));
    }

    [Fact]
    public async Task GivenADueDateThatIsNotTheCurrentOne_WhenSnoozing_ThenACodedProblemIsReturned()
    {
        var (token, tank, _) = await TankDueTodayAsync();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, token,
            new { tankId = tank, kind = "water_change", dueDate = "2020-01-01", timeZone = "Etc/UTC" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "web_push_snooze_invalid");
    }

    [Theory]
    [InlineData("water_change", "2026-13-40", "Etc/UTC", "web_push_snooze_invalid")]
    [InlineData("water_change", "2026-10-16", "Mars/Olympus", "web_push_snooze_invalid")]
    [InlineData("feeding", "2026-10-16", "Etc/UTC", "care_kind_invalid")]
    public async Task GivenInvalidInput_WhenSnoozing_ThenACodedProblemIsReturned(string kind, string dueDate, string timeZone, string code)
    {
        var (token, tank, _) = await TankDueTodayAsync();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, token, new { tankId = tank, kind, dueDate, timeZone });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task GivenNoLogin_WhenSnoozing_ThenACodedAuthProblemIsReturned()
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Snooze, null,
            new { tankId = 1, kind = "water_change", dueDate = "2026-10-16", timeZone = "Etc/UTC" });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "care_auth_required");
    }
}
