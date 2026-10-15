using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class CarePlanRuleConstraintTests(PostgresFixture fixture)
{
    private static CarePlanRule Rule(long tankId, CareKind kind = CareKind.WaterChange, int days = 7, string reason = "cichlid_default",
        string? parameters = null) => new()
    {
        TankId = tankId, Kind = kind, RhythmDays = days, Enabled = true, ReasonCode = reason,
        ReasonParametersJson = parameters, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<long> TankAsync()
    {
        await using var context = fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        return (await TestDataBuilder.CreateTankAsync(context, profile.Id)).Id;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    public async Task Rhythm_within_one_to_ninety_days_is_stored(int days)
    {
        var tank = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRules.Add(Rule(tank, days: days, parameters: "{\"species\":\"mbuna\",\"count\":\"12\"}"));
        await context.SaveChangesAsync();
        Assert.Equal(days, (await context.CarePlanRules.AsNoTracking().SingleAsync(r => r.TankId == tank)).RhythmDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    [InlineData(-5)]
    public async Task Rhythm_outside_one_to_ninety_days_is_rejected(int days)
    {
        var tank = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRules.Add(Rule(tank, days: days));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_rule_of_the_same_kind_for_a_tank_is_rejected()
    {
        var tank = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRules.Add(Rule(tank));
        context.CarePlanRules.Add(Rule(tank, CareKind.Measurement));
        await context.SaveChangesAsync();
        await using var second = fixture.CreateContext();
        second.CarePlanRules.Add(Rule(tank, days: 3));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Cichlid")]
    [InlineData("")]
    [InlineData("1abc")]
    public async Task A_reason_code_that_is_not_lower_snake_case_is_rejected(string reason)
    {
        var tank = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRules.Add(Rule(tank, reason: reason));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Reason_parameters_that_are_not_a_json_object_are_rejected()
    {
        var tank = await TankAsync();
        await using var context = fixture.CreateContext();
        context.CarePlanRules.Add(Rule(tank, parameters: "[1,2]"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
