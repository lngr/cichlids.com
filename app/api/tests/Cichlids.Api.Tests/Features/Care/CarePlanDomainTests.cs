using Cichlids.Domain;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Care;

public sealed class CarePlanDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static CareRule Rule(CareKind kind, int days, bool enabled = true) =>
        new(kind, days, enabled, CareReasons.MemberChoice, null);

    [Fact]
    public void DefaultRule_IsWaterChangeEverySevenDaysWithTheCichlidReason_AndMeasurementHasNone()
    {
        var rule = CarePlans.DefaultFor(CareKind.WaterChange);
        Assert.NotNull(rule);
        Assert.Equal(7, rule.RhythmDays);
        Assert.True(rule.Enabled);
        Assert.Equal("cichlid_default", rule.ReasonCode);
        Assert.Null(CarePlans.DefaultFor(CareKind.Measurement));
    }

    [Theory]
    [InlineData(0, 7, CareDueState.DueIn, 7)]
    [InlineData(3, 7, CareDueState.DueIn, 4)]
    [InlineData(6, 7, CareDueState.DueIn, 1)]
    [InlineData(7, 7, CareDueState.DueToday, 0)]
    [InlineData(8, 7, CareDueState.Overdue, 1)]
    [InlineData(20, 14, CareDueState.Overdue, 6)]
    [InlineData(1, 1, CareDueState.DueToday, 0)]
    [InlineData(0, 90, CareDueState.DueIn, 90)]
    public void DueFor_ComparesTheLocalDateWithTheLastEntryPlusTheRhythm(int daysSinceLast, int rhythm, CareDueState state, int days)
    {
        var last = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero).AddDays(-daysSinceLast);
        var due = CarePlans.DueFor(Rule(CareKind.WaterChange, rhythm), last, Today, 0);
        Assert.NotNull(due);
        Assert.Equal(state, due.Value.State);
        Assert.Equal(days, due.Value.Days);
        Assert.Equal(DateOnly.FromDateTime(last.UtcDateTime).AddDays(rhythm), due.Value.DueDate);
    }

    [Fact]
    public void DueFor_UsesTheMembersLocalDayOfTheLastEntry()
    {
        var last = new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.Zero);
        var utc = CarePlans.DueFor(Rule(CareKind.WaterChange, 7), last, Today, 0);
        var plusTwo = CarePlans.DueFor(Rule(CareKind.WaterChange, 7), last, Today, 120);
        var minusFive = CarePlans.DueFor(Rule(CareKind.WaterChange, 7), last, Today, -300);
        Assert.Equal(CareDueState.DueToday, utc!.Value.State);
        Assert.Equal(CareDueState.DueIn, plusTwo!.Value.State);
        Assert.Equal(1, plusTwo.Value.Days);
        Assert.Equal(CareDueState.DueToday, minusFive!.Value.State);
        Assert.Equal(new DateOnly(2026, 10, 9), CarePlans.LocalDate(last, 120));
    }

    [Theory]
    [InlineData(CareKind.WaterChange)]
    [InlineData(CareKind.Measurement)]
    public void DueFor_WithoutAnyEntry_IsNeverLogged(CareKind kind)
    {
        var due = CarePlans.DueFor(Rule(kind, 7), null, Today, 0);
        Assert.Equal(CareDueState.NeverLogged, due!.Value.State);
        Assert.Null(due.Value.DueDate);
    }

    [Theory]
    [InlineData(CareKind.WaterChange)]
    [InlineData(CareKind.Measurement)]
    public void DueFor_ForADisabledRule_HasNoDueState(CareKind kind)
    {
        Assert.Null(CarePlans.DueFor(Rule(kind, 7, enabled: false), DateTimeOffset.UnixEpoch, Today, 0));
        Assert.Null(CarePlans.DueFor(Rule(kind, 7, enabled: false), null, Today, 0));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(90, true)]
    [InlineData(0, false)]
    [InlineData(91, false)]
    [InlineData(-3, false)]
    public void IsValidRhythm_AcceptsOneToNinetyDays(int days, bool valid) => Assert.Equal(valid, CarePlans.IsValidRhythm(days));

    [Theory]
    [InlineData(0, true)]
    [InlineData(840, true)]
    [InlineData(-840, true)]
    [InlineData(841, false)]
    [InlineData(-841, false)]
    public void IsValidUtcOffset_AcceptsRealOffsets(int minutes, bool valid) => Assert.Equal(valid, CarePlans.IsValidUtcOffset(minutes));
}
