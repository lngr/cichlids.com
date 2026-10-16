using System.Text.Json;
using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

/// <summary>
/// The due care of a member names the reminder days of a running water change streak: from the
/// last day of the rhythm through the last day of the grace, none while a vacation pauses it.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.18")]
public sealed class CareDueStreakTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset WaterChange = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Vacations.Where(v => db.Tanks.Any(t => _tanks.Contains(t.Id) && t.ProfileId == v.ProfileId)).ExecuteDeleteAsync();
        await RemoveTanksAsync(db, _tanks);
    }

    private async Task<(string Token, long Tank)> TankWithWaterChangeAsync()
    {
        var (token, tank) = await CareTestSupport.OwnTankAsync(fixture.Client);
        _tanks.Add(tank);
        await LogAsync(fixture.Client, token, tank, new { kind = "water_change", occurredAt = WaterChange });
        return (token, tank);
    }

    private async Task<JsonElement> ItemAsync(string token, long tank, string kind, string date)
    {
        using var due = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/care/due?date={date}", token);
        return (await BodyAsync(due)).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("tankId").GetInt64() == tank && i.GetProperty("kind").GetString() == kind);
    }

    [Theory]
    [InlineData("2026-10-12")]
    [InlineData("2026-10-15")]
    [InlineData("2026-10-17")]
    [InlineData("2026-10-18")]
    public async Task GivenARunningStreakOnAWeeklyPlan_WhenReadingTheDueCare_ThenTheReminderDaysAreDaySevenThroughDayTen(string date)
    {
        var (token, tank) = await TankWithWaterChangeAsync();

        var streak = (await ItemAsync(token, tank, "water_change", date)).GetProperty("streak");

        Assert.Equal("running", streak.GetProperty("state").GetString());
        Assert.Equal("2026-10-15", streak.GetProperty("reminderFrom").GetString());
        Assert.Equal("2026-10-18", streak.GetProperty("deadline").GetString());
    }

    [Fact]
    public async Task GivenABrokenStreak_WhenReadingTheDueCare_ThenTheWaterChangeCarriesNoStreak()
    {
        var (token, tank) = await TankWithWaterChangeAsync();

        var item = await ItemAsync(token, tank, "water_change", "2026-10-19");

        Assert.False(item.TryGetProperty("streak", out var streak) && streak.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task GivenAnActiveVacation_WhenReadingTheDueCare_ThenTheStreakIsPausedWithoutReminderDays()
    {
        var (token, tank) = await TankWithWaterChangeAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var profile = await db.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync();
            db.Vacations.Add(new Vacation { ProfileId = profile, StartDate = new DateOnly(2026, 10, 14), EndDate = null, CreatedAt = WaterChange });
            await db.SaveChangesAsync();
        }

        var streak = (await ItemAsync(token, tank, "water_change", "2026-10-15")).GetProperty("streak");

        Assert.Equal("paused", streak.GetProperty("state").GetString());
        Assert.True(streak.GetProperty("reminderFrom").ValueKind is JsonValueKind.Null || !streak.TryGetProperty("reminderFrom", out _));
    }

    [Fact]
    public async Task GivenALoggedWaterChange_WhenReadingTheDueCare_ThenTheWindowMovesToTheNewPeriod()
    {
        var (token, tank) = await TankWithWaterChangeAsync();
        await LogAsync(fixture.Client, token, tank, new { kind = "water_change", occurredAt = DateTimeOffset.Parse("2026-10-16T10:00:00Z") });

        var streak = (await ItemAsync(token, tank, "water_change", "2026-10-16")).GetProperty("streak");

        Assert.Equal("2026-10-23", streak.GetProperty("reminderFrom").GetString());
        Assert.Equal("2026-10-26", streak.GetProperty("deadline").GetString());
    }
}
