using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class StreakConstraintTests(PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    private async Task<(long Profile, long Tank)> TankAsync()
    {
        await using var context = fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        return (profile.Id, (await TestDataBuilder.CreateTankAsync(context, profile.Id)).Id);
    }

    private async Task<long> EntryAsync(long tank, long profile)
    {
        await using var context = fixture.CreateContext();
        var entry = new CareEntry
        {
            TankId = tank, AuthorProfileId = profile, Kind = CareKind.WaterChange,
            OccurredAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow,
        };
        context.CareEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    private static Vacation Vacation(long profile, DateOnly start, DateOnly? end) =>
        new() { ProfileId = profile, StartDate = start, EndDate = end, CreatedAt = DateTimeOffset.UtcNow };

    private static StreakFreeze Freeze(long tank, long entry, DateOnly start, DateOnly end, int tankYear = 1) =>
        new() { TankId = tank, CareEntryId = entry, PeriodStart = start, PeriodEnd = end, TankYear = tankYear, CreatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task A_vacation_is_stored_with_and_without_an_end()
    {
        var (profile, _) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.Vacations.Add(Vacation(profile, Day, Day.AddDays(3)));
        context.Vacations.Add(Vacation(profile, Day.AddDays(10), null));
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.Vacations.CountAsync(v => v.ProfileId == profile));
    }

    [Fact]
    public async Task A_vacation_may_end_on_its_first_day()
    {
        var (profile, _) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.Vacations.Add(Vacation(profile, Day, Day));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_vacation_that_ends_before_it_starts_is_rejected()
    {
        var (profile, _) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.Vacations.Add(Vacation(profile, Day, Day.AddDays(-1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_member_has_at_most_one_active_vacation()
    {
        var (profile, _) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.Vacations.Add(Vacation(profile, Day, null));
        await context.SaveChangesAsync();
        await using var second = fixture.CreateContext();
        second.Vacations.Add(Vacation(profile, Day.AddDays(5), null));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_freeze_records_the_period_of_a_water_change_once()
    {
        var (profile, tank) = await TankAsync();
        var entry = await EntryAsync(tank, profile);
        await using var context = fixture.CreateContext();
        context.StreakFreezes.Add(Freeze(tank, entry, Day, Day.AddDays(7)));
        await context.SaveChangesAsync();
        await using var second = fixture.CreateContext();
        second.StreakFreezes.Add(Freeze(tank, entry, Day, Day.AddDays(7)));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_freeze_needs_a_period_that_does_not_end_before_it_starts_and_a_positive_tank_year()
    {
        var (profile, tank) = await TankAsync();
        var first = await EntryAsync(tank, profile);
        var second = await EntryAsync(tank, profile);
        await using var context = fixture.CreateContext();
        context.StreakFreezes.Add(Freeze(tank, first, Day, Day.AddDays(-2)));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await using var other = fixture.CreateContext();
        other.StreakFreezes.Add(Freeze(tank, second, Day, Day.AddDays(7), tankYear: 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_the_water_change_removes_its_freeze()
    {
        var (profile, tank) = await TankAsync();
        var entry = await EntryAsync(tank, profile);
        await using (var context = fixture.CreateContext())
        {
            context.StreakFreezes.Add(Freeze(tank, entry, Day, Day.AddDays(7)));
            await context.SaveChangesAsync();
        }
        await using var verify = fixture.CreateContext();
        await verify.CareEntries.Where(e => e.Id == entry).ExecuteDeleteAsync();
        Assert.False(await verify.StreakFreezes.AnyAsync(f => f.TankId == tank));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    public async Task A_rhythm_change_within_one_to_ninety_days_is_stored(int days)
    {
        var (_, tank) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRuleChanges.Add(new CarePlanRuleChange { TankId = tank, Kind = CareKind.WaterChange, RhythmDays = days, Enabled = true, At = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task A_rhythm_change_outside_one_to_ninety_days_is_rejected(int days)
    {
        var (_, tank) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRuleChanges.Add(new CarePlanRuleChange { TankId = tank, Kind = CareKind.WaterChange, RhythmDays = days, Enabled = true, At = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
