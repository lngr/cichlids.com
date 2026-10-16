using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

/// <summary>
/// A decommissioned tank has a paused water change streak: it neither counts nor breaks, a water change
/// on a decommissioned day is ignored and the period runs from the day the tank is put back into
/// operation. The tank year counts operating days.
/// </summary>
[Trait("Story", "task-6")]
public sealed class WaterChangeDecommissionTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    private static DateOnly Day(int offset) => Start.AddDays(offset);

    private static WaterChangeRecord Change(long id, int offset, bool verified = true) =>
        new(id, new DateTimeOffset(Day(offset).Year, Day(offset).Month, Day(offset).Day, 12, 0, 0, TimeSpan.Zero), verified);

    private static DecommissionSpan Span(int first, int? end) => new(Day(first), end is { } e ? Day(e) : null);

    private static WaterChangeStreak Run(int[] changeDays, int today, IReadOnlyList<DecommissionSpan> decommissions, bool verified = true,
        IReadOnlyList<VacationSpan>? vacations = null) =>
        WaterChangeStreaks.Evaluate(changeDays.Select((offset, index) => Change(index + 1, offset, verified)).ToList(),
            [new RhythmChange(DateTimeOffset.UnixEpoch, 7, true)], vacations ?? [], new HashSet<long>(), 2, Start, Day(today), 0, decommissions);

    [Fact]
    public void ADecommissionedTankPausesTheStreakWhateverTheLengthOfTheTime()
    {
        var streak = Run([0, 7], 400, [Span(10, null)]);
        Assert.Equal(WaterChangeStreakState.Paused, streak.State);
        Assert.Equal(1, streak.Current);
        Assert.Null(streak.Deadline);
    }

    [Fact]
    public void ThePeriodRunsFromTheDayTheTankIsPutBackIntoOperation()
    {
        var streak = Run([0], 30, [Span(3, 20)]);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(Day(30), streak.Deadline);
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 31, [Span(3, 20)]).State);
    }

    [Fact]
    public void TheStreakContinuesWithTheNextWaterChangeAfterTheDecommissioning()
    {
        var streak = Run([0, 7, 25], 25, [Span(10, 20)]);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(2, streak.Current);
        Assert.Equal([PeriodOutcome.Verified, PeriodOutcome.Verified], streak.Periods.Select(p => p.Outcome));
    }

    [Fact]
    public void AWaterChangeOnADecommissionedDayDoesNotCountForTheStreak()
    {
        var streak = Run([0, 12, 25], 25, [Span(10, 20)]);
        Assert.Single(streak.Periods);
        Assert.Equal(Day(0), streak.Periods[0].Start);
        Assert.Equal(Day(25), streak.Periods[0].End);
        Assert.Equal(1, streak.Current);
    }

    [Fact]
    public void ADecommissioningDoesNotExtendTheDeadlineOfAPeriodThatWasAlreadyMissed()
    {
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 40, [Span(12, 30)]).State);
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 20, [Span(12, null)]).State);
        var late = Run([0, 35], 35, [Span(12, 30)]);
        Assert.Equal(PeriodOutcome.Missed, late.Periods[0].Outcome);
        Assert.Equal(0, late.Current);
    }

    [Fact]
    public void ADecommissioningThatBeginsAfterTheLastDayInTimeFindsThePeriodMissed()
    {
        Assert.Equal(WaterChangeStreakState.Paused, Run([0], 15, [Span(11, null)]).State);
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 15, [Span(12, null)]).State);
    }

    [Fact]
    public void TheDecommissionedDaysDoNotBreakTheRunWhenSeveralDecommissioningsFollowEachOther()
    {
        var streak = Run([0, 7, 36], 36, [Span(8, 15), Span(20, 30)]);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void TheStreakIsTheSameWithoutDecommissionings()
    {
        var without = Run([0, 7, 14], 20, []);
        var withNull = WaterChangeStreaks.Evaluate([Change(1, 0), Change(2, 7), Change(3, 14)],
            [new RhythmChange(DateTimeOffset.UnixEpoch, 7, true)], [], new HashSet<long>(), 2, Start, Day(20), 0);
        Assert.Equal(without.Current, withNull.Current);
        Assert.Equal(without.Deadline, withNull.Deadline);
    }

    [Fact]
    public void TheFreezesOfATankYearMoveWithTheDecommissionedDays()
    {
        var changes = new List<WaterChangeRecord> { Change(1, 380), Change(2, 387, verified: false), Change(3, 394, verified: false) };
        WaterChangeStreak Streak(IReadOnlyList<DecommissionSpan> decommissions) => WaterChangeStreaks.Evaluate(changes,
            [new RhythmChange(DateTimeOffset.UnixEpoch, 7, true)], [], new HashSet<long>(), 2, Start, Day(394), 0, decommissions);
        Assert.Equal([2, 2], Streak([]).Freezes.Select(f => f.TankYear));
        Assert.Equal([1, 1], Streak([Span(100, 200)]).Freezes.Select(f => f.TankYear));
    }

    [Theory]
    [InlineData(364, 1)]
    [InlineData(365, 2)]
    [InlineData(465, 2)]
    public void ATankYearWithoutDecommissioningsStartsOnTheCalendarAnniversary(int day, int expected) =>
        Assert.Equal(expected, WaterChangeStreaks.TankYear(Start, [], Day(day)));

    [Theory]
    [InlineData(99, 1)]
    [InlineData(300, 1)]
    [InlineData(464, 1)]
    [InlineData(465, 2)]
    public void TheTankYearStartsOnTheAnniversaryOfTheCreationDateMovedByTheDecommissionedDays(int day, int expected) =>
        Assert.Equal(expected, WaterChangeStreaks.TankYear(Start, [Span(100, 200)], Day(day)));

    [Fact]
    public void ThePastTankYearsDoNotChangeWhenALaterDecommissioningBegins()
    {
        Assert.Equal(WaterChangeStreaks.TankYear(Start, [], Day(400)), WaterChangeStreaks.TankYear(Start, [Span(401, 500)], Day(400)));
    }

    [Fact]
    public void ATankRunForSixMonthsDecommissionedForOneAndRunForOneMoreHasSevenMonthsOfOperation()
    {
        var created = new DateOnly(2026, 1, 15);
        var spans = new[] { new DecommissionSpan(new DateOnly(2026, 7, 15), new DateOnly(2026, 8, 15)) };
        var operation = Decommissions.OperationOn(created, spans, new DateOnly(2026, 9, 15));
        Assert.Equal(7, operation.OperatingMonths);
        Assert.Equal(31, operation.DecommissionedDays);
        Assert.Equal(243 - 31, operation.OperatingDays);
    }

    [Fact]
    public void AMonthOfRestOfThirtyOneDaysTakesOneMonthOffTheOperatingTime()
    {
        var created = new DateOnly(2026, 2, 16);
        var operation = Decommissions.OperationOn(created, [new DecommissionSpan(new DateOnly(2026, 8, 16), new DateOnly(2026, 9, 16))], new DateOnly(2026, 10, 16));
        Assert.Equal(7, operation.OperatingMonths);
        Assert.Equal(211, operation.OperatingDays);
        Assert.Equal(31, operation.DecommissionedDays);
    }

    [Fact]
    public void TheDaysLeftOverInSeveralStretchesOfOperationAddUpToMonthsOfThirtyDays()
    {
        var operation = Decommissions.OperationOn(new DateOnly(2026, 1, 1), [new DecommissionSpan(new DateOnly(2026, 1, 21), new DateOnly(2026, 2, 5))], new DateOnly(2026, 2, 20));
        Assert.Equal(35, operation.OperatingDays);
        Assert.Equal(1, operation.OperatingMonths);
        var shorter = Decommissions.OperationOn(new DateOnly(2026, 1, 1), [new DecommissionSpan(new DateOnly(2026, 1, 21), new DateOnly(2026, 2, 5))], new DateOnly(2026, 2, 10));
        Assert.Equal(0, shorter.OperatingMonths);
    }

    [Fact]
    public void OverlappingDecommissioningsCountEachDayOnce()
    {
        var operation = Decommissions.OperationOn(new DateOnly(2026, 1, 1), [new DecommissionSpan(new DateOnly(2026, 1, 11), new DateOnly(2026, 1, 21)), new DecommissionSpan(new DateOnly(2026, 1, 16), null)], new DateOnly(2026, 2, 1));
        Assert.Equal(10, operation.OperatingDays);
        Assert.Equal(21, operation.DecommissionedDays);
    }

    [Fact]
    public void AnActiveDecommissioningCountsUpToTodayAndTheOperatingTimeStandsStill()
    {
        var created = new DateOnly(2026, 1, 15);
        var spans = new[] { new DecommissionSpan(new DateOnly(2026, 4, 15), null) };
        var early = Decommissions.OperationOn(created, spans, new DateOnly(2026, 5, 15));
        var late = Decommissions.OperationOn(created, spans, new DateOnly(2026, 9, 15));
        Assert.Equal(early.OperatingDays, late.OperatingDays);
        Assert.Equal(3, late.OperatingMonths);
        Assert.Equal(153, late.DecommissionedDays);
    }

    [Fact]
    public void ATankWithoutDecommissioningsCountsCalendarMonths()
    {
        var operation = Decommissions.OperationOn(new DateOnly(2026, 1, 31), [], new DateOnly(2026, 3, 30));
        Assert.Equal(1, operation.OperatingMonths);
        Assert.Equal(58, operation.OperatingDays);
        Assert.Equal(new TankOperation(0, 0, 0), Decommissions.OperationOn(new DateOnly(2026, 1, 31), [], new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void TheEndDayBelongsToTheOperatingTimeAndTheStartDayToTheDecommissioning()
    {
        var spans = new[] { new DecommissionSpan(Day(10), Day(20)) };
        Assert.False(Decommissions.Covers(spans, Day(9)));
        Assert.True(Decommissions.Covers(spans, Day(10)));
        Assert.True(Decommissions.Covers(spans, Day(19)));
        Assert.False(Decommissions.Covers(spans, Day(20)));
        Assert.Equal(0, Decommissions.DaysBetween([new DecommissionSpan(Day(10), Day(10))], Day(0), Day(30)));
    }
}
