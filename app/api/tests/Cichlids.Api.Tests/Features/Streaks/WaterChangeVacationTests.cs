using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

/// <summary>
/// Vacation days count like any other day of a water change period. The deadline of a period is the
/// later of its regular deadline and the last vacation day plus the grace of the plan.
/// </summary>
[Trait("Story", "task-5.21")]
public sealed class WaterChangeVacationTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    private static DateOnly Day(int offset) => Start.AddDays(offset);

    private static WaterChangeRecord Change(long id, int offset, bool verified = true) =>
        new(id, new DateTimeOffset(Day(offset).Year, Day(offset).Month, Day(offset).Day, 12, 0, 0, TimeSpan.Zero), verified);

    private static WaterChangeStreak Run(int[] changeDays, int today, IReadOnlyList<VacationSpan> vacations, int rhythm = 7,
        IReadOnlyList<RhythmChange>? rhythmChanges = null, bool verified = true) =>
        WaterChangeStreaks.Evaluate(changeDays.Select((offset, index) => Change(index + 1, offset, verified)).ToList(),
            rhythmChanges ?? [new RhythmChange(DateTimeOffset.UnixEpoch, rhythm, true)], vacations, new HashSet<long>(), 2, Start, Day(today), 0);

    private static VacationSpan Vacation(int first, int? last) => new(Day(first), last is { } l ? Day(l) : null);

    [Theory]
    [InlineData(2, 6, 10)]
    [InlineData(5, 20, 23)]
    [InlineData(8, 9, 12)]
    [InlineData(1, 3, 10)]
    [InlineData(7, 7, 10)]
    [InlineData(10, 10, 13)]
    public void TheDeadlineIsTheLaterOfTheRegularDeadlineAndTheLastVacationDayPlusTheGrace(int first, int last, int deadline)
    {
        var streak = Run([0], deadline, [Vacation(first, last)]);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(Day(deadline), streak.Deadline);
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], deadline + 1, [Vacation(first, last)]).State);
    }

    [Fact]
    public void AVacationThatStartsAfterTheRegularDeadlineChangesNothing()
    {
        var streak = Run([0], 11, [Vacation(11, 20)]);
        Assert.Equal(WaterChangeStreakState.Broken, streak.State);
    }

    [Fact]
    public void ABreakAfterAVacationFallsOnTheDayAfterTheNewDeadline()
    {
        Assert.Equal(WaterChangeStreakState.Running, Run([0], 23, [Vacation(5, 20)]).State);
        var broken = Run([0], 24, [Vacation(5, 20)]);
        Assert.Equal(WaterChangeStreakState.Broken, broken.State);
        Assert.Equal(0, broken.Current);
    }

    [Fact]
    public void TheClosingWaterChangeAfterAVacationCountsLikeAnyOther()
    {
        var verified = Run([0, 23], 23, [Vacation(5, 20)]);
        Assert.Equal(PeriodOutcome.Verified, verified.Periods.Single().Outcome);
        Assert.Equal(1, verified.Current);
        Assert.Equal(1, verified.CurrentVerified);
        var frozen = Run([0, 23], 23, [Vacation(5, 20)], verified: false);
        Assert.Equal(PeriodOutcome.Frozen, frozen.Periods.Single().Outcome);
        Assert.Equal(1, frozen.Current);
        Assert.Single(frozen.Freezes);
    }

    [Fact]
    public void AWaterChangeAfterTheNewDeadlineMissesThePeriod()
    {
        var streak = Run([0, 24], 24, [Vacation(5, 20)]);
        Assert.Equal(PeriodOutcome.Missed, streak.Periods.Single().Outcome);
        Assert.Equal(0, streak.Current);
    }

    [Fact]
    public void AVacationNeverAddsToTheLengthOfTheStreak()
    {
        var streak = Run([0, 7, 14], 14, [Vacation(20, 40)]);
        Assert.Equal(2, streak.Current);
        var across = Run([0, 7, 23, 30], 30, [Vacation(9, 20)]);
        Assert.Equal([PeriodOutcome.Verified, PeriodOutcome.Verified, PeriodOutcome.Verified], across.Periods.Select(p => p.Outcome));
        Assert.Equal(3, across.Current);
    }

    [Fact]
    public void ADayOfVacationInsideThePeriodCountsLikeANormalDay()
    {
        var vacation = Vacation(2, 4);
        Assert.Equal(PeriodOutcome.Verified, Run([0, 10], 10, [vacation]).Periods.Single().Outcome);
        Assert.Equal(PeriodOutcome.Missed, Run([0, 11], 11, [vacation]).Periods.Single().Outcome);
    }

    [Fact]
    public void ASecondVacationThatReachesTheExtendedDeadlineExtendsItAgain()
    {
        var vacations = new List<VacationSpan> { Vacation(5, 20), Vacation(23, 30) };
        var streak = Run([0], 33, vacations);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(Day(33), streak.Deadline);
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 34, vacations).State);
    }

    [Fact]
    public void ASecondVacationAfterTheExtendedDeadlineChangesNothing()
    {
        var vacations = new List<VacationSpan> { Vacation(5, 20), Vacation(24, 30) };
        Assert.Equal(WaterChangeStreakState.Broken, Run([0], 24, vacations).State);
    }

    [Fact]
    public void AnActiveVacationPausesTheStreakWithoutAReminderWindow()
    {
        var streak = Run([0], 15, [Vacation(5, null)]);
        Assert.Equal(WaterChangeStreakState.Paused, streak.State);
        Assert.Null(streak.Deadline);
        Assert.Null(streak.FirstDayAfterVacation);
    }

    [Fact]
    public void AVacationThatIsStillRunningDoesNotMissAClosingWaterChange()
    {
        var streak = Run([0, 40], 40, [Vacation(5, null)]);
        Assert.Equal(PeriodOutcome.Verified, streak.Periods.Single().Outcome);
    }

    [Fact]
    public void TheDayAfterTheVacationIsTheFirstReminderDayWhenItFallsAfterTheRegularStart()
    {
        var long_ = Run([0], 21, [Vacation(5, 20)]);
        Assert.Equal(Day(21), long_.FirstDayAfterVacation);
        Assert.Equal(Day(23), long_.Deadline);
        var short_ = Run([0], 10, [Vacation(8, 9)]);
        Assert.Equal(Day(10), short_.FirstDayAfterVacation);
        Assert.Equal(Day(12), short_.Deadline);
    }

    [Fact]
    public void WithoutAVacationOfThePeriodThereIsNoDayAfterVacation()
    {
        Assert.Null(Run([0], 3, []).FirstDayAfterVacation);
        Assert.Null(Run([10], 12, [Vacation(2, 4)]).FirstDayAfterVacation);
        Assert.Null(Run([0], 3, [Vacation(11, 12)]).FirstDayAfterVacation);
    }

    [Fact]
    public void TheGraceOfTheRhythmOfTheLastVacationDayApplies()
    {
        var streak = Run([0], 35, [Vacation(10, 30)], rhythm: 14);
        Assert.Equal(Day(35), streak.Deadline);
    }
}
