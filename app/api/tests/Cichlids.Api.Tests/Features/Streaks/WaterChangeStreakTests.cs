using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

public sealed class WaterChangeStreakTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    private static DateOnly Day(int offset) => Start.AddDays(offset);

    private static WaterChangeRecord Change(long id, DateOnly date, bool verified = true, int hour = 12) =>
        new(id, new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero), verified);

    private static List<WaterChangeRecord> Verified(params int[] dayOffsets) =>
        dayOffsets.Select((offset, index) => Change(index + 1, Day(offset))).ToList();

    private static WaterChangeStreak Run(IReadOnlyList<WaterChangeRecord> changes, DateOnly today, int rhythm = 7,
        IReadOnlyList<VacationSpan>? vacations = null, IReadOnlySet<long>? recorded = null, int freezes = 2,
        IReadOnlyList<RhythmChange>? rhythmChanges = null, int utcOffsetMinutes = 0, DateOnly? tankStart = null) =>
        WaterChangeStreaks.Evaluate(changes, rhythmChanges ?? [new RhythmChange(DateTimeOffset.UnixEpoch, rhythm, true)],
            vacations ?? [], recorded ?? new HashSet<long>(), freezes, tankStart ?? Start, today, utcOffsetMinutes);

    [Theory]
    [InlineData(7, 3)]
    [InlineData(10, 4)]
    [InlineData(14, 5)]
    [InlineData(1, 1)]
    [InlineData(90, 30)]
    public void GraceIsOneThirdOfTheRhythmRoundedUp(int rhythm, int grace) =>
        Assert.Equal(grace, WaterChangeStreaks.GraceDays(rhythm));

    [Theory]
    [InlineData(7, 7, true)]
    [InlineData(7, 10, true)]
    [InlineData(7, 11, false)]
    [InlineData(10, 14, true)]
    [InlineData(10, 15, false)]
    [InlineData(14, 19, true)]
    [InlineData(14, 20, false)]
    [InlineData(7, 1, true)]
    public void AWaterChangeKeepsThePeriodUpToTheRhythmPlusGrace(int rhythm, int daysLater, bool kept)
    {
        var streak = Run([Change(1, Day(0)), Change(2, Day(daysLater))], Day(daysLater), rhythm);
        Assert.Equal(kept ? 1 : 0, streak.Current);
        Assert.Equal(kept ? PeriodOutcome.Verified : PeriodOutcome.Missed, streak.Periods.Single().Outcome);
    }

    [Fact]
    public void WithoutAnyWaterChangeThereIsNoHistory()
    {
        var streak = Run([], Day(5));
        Assert.Equal(WaterChangeStreakState.NoHistory, streak.State);
        Assert.Equal(0, streak.Current);
        Assert.Null(streak.Deadline);
        Assert.Null(streak.LastWaterChange);
        Assert.Equal(2, streak.FreezesLeft);
    }

    [Fact]
    public void TheFirstWaterChangeStartsARunningStreakWithTheDeadlineOfItsPeriod()
    {
        var streak = Run(Verified(2), Day(4));
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(0, streak.Current);
        Assert.Equal(Day(12), streak.Deadline);
        Assert.Equal(Day(2), streak.LastWaterChange);
    }

    [Fact]
    public void VerifiedWaterChangesInTimeBuildUpTheStreakEachPeriodCountsOnce()
    {
        var streak = Run(Verified(0, 7, 15, 22, 29), Day(30));
        Assert.Equal(4, streak.Current);
        Assert.Equal(4, streak.CurrentVerified);
        Assert.Equal(4, streak.Longest);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(Day(39), streak.Deadline);
        Assert.All(streak.Periods, p => Assert.Equal(PeriodOutcome.Verified, p.Outcome));
    }

    [Fact]
    public void TheDeadlineIsTheLastDayOfRhythmPlusGraceAndAnyDayBeforeItKeepsTheStreakRunning()
    {
        var onDeadline = Run(Verified(0, 7), Day(17));
        Assert.Equal(WaterChangeStreakState.Running, onDeadline.State);
        Assert.Equal(Day(17), onDeadline.Deadline);
        Assert.Equal(1, onDeadline.Current);
        var after = Run(Verified(0, 7), Day(18));
        Assert.Equal(WaterChangeStreakState.Broken, after.State);
        Assert.Equal(0, after.Current);
        Assert.Equal(1, after.Longest);
        Assert.Null(after.Deadline);
    }

    [Fact]
    public void AMissedPeriodBreaksTheStreakAndTheNextWaterChangeStartsANewOne()
    {
        var streak = Run(Verified(0, 7, 14, 21, 40, 47), Day(48));
        Assert.Equal(1, streak.Current);
        Assert.Equal(3, streak.Longest);
        Assert.Equal([PeriodOutcome.Verified, PeriodOutcome.Verified, PeriodOutcome.Verified, PeriodOutcome.Missed, PeriodOutcome.Verified],
            streak.Periods.Select(p => p.Outcome));
    }

    [Fact]
    public void TheLongestStreakKeepsTheRecordAfterABreak()
    {
        var streak = Run(Verified(0, 7, 14, 21, 28, 60), Day(61));
        Assert.Equal(0, streak.Current);
        Assert.Equal(4, streak.Longest);
    }

    [Fact]
    public void AnUnverifiedWaterChangeKeepsThePeriodOnlyThroughAFreezeWhichIsRecorded()
    {
        var changes = new List<WaterChangeRecord> { Change(1, Day(0)), Change(2, Day(7), verified: false), Change(3, Day(14)) };
        var streak = Run(changes, Day(14));
        Assert.Equal(2, streak.Current);
        Assert.Equal(1, streak.CurrentVerified);
        Assert.Equal(PeriodOutcome.Frozen, streak.Periods[0].Outcome);
        var freeze = Assert.Single(streak.Freezes);
        Assert.Equal(2, freeze.WaterChangeId);
        Assert.Equal(1, freeze.TankYear);
        Assert.Equal(Day(0), freeze.PeriodStart);
        Assert.Equal(Day(7), freeze.PeriodEnd);
        Assert.False(freeze.Recorded);
        Assert.Equal(1, streak.FreezesLeft);
    }

    [Fact]
    public void AFreezeThatIsAlreadyRecordedIsKeptAndCountedAsRecorded()
    {
        var changes = new List<WaterChangeRecord> { Change(1, Day(0)), Change(2, Day(7), verified: false) };
        var streak = Run(changes, Day(8), recorded: new HashSet<long> { 2 });
        var freeze = Assert.Single(streak.Freezes);
        Assert.True(freeze.Recorded);
        Assert.Equal(1, streak.Current);
        Assert.Equal(1, streak.FreezesLeft);
    }

    [Fact]
    public void RecordedFreezesCountAgainstTheAllotmentEvenWhenAnEarlierPeriodNeedsOne()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, Day(0)), Change(2, Day(7), verified: false), Change(3, Day(14)),
            Change(4, Day(21), verified: false), Change(5, Day(28), verified: false),
        };
        var streak = Run(changes, Day(28), recorded: new HashSet<long> { 4, 5 });
        Assert.Equal(PeriodOutcome.Unverified, streak.Periods[0].Outcome);
        Assert.Equal(0, streak.FreezesLeft);
        Assert.Equal(2, streak.Freezes.Count(f => f.Recorded));
    }

    [Fact]
    public void AnUnverifiedPeriodWithoutAFreezeLeftBreaksTheStreak()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, Day(0)), Change(2, Day(7), verified: false), Change(3, Day(14), verified: false),
            Change(4, Day(21), verified: false), Change(5, Day(28)),
        };
        var streak = Run(changes, Day(28));
        Assert.Equal([PeriodOutcome.Frozen, PeriodOutcome.Frozen, PeriodOutcome.Unverified, PeriodOutcome.Verified],
            streak.Periods.Select(p => p.Outcome));
        Assert.Equal(1, streak.Current);
        Assert.Equal(2, streak.Longest);
        Assert.Equal(0, streak.FreezesLeft);
        Assert.Equal(2, streak.Freezes.Count);
    }

    [Fact]
    public void WithAnAllotmentOfZeroAnUnverifiedWaterChangeBreaks()
    {
        var changes = new List<WaterChangeRecord> { Change(1, Day(0)), Change(2, Day(7), verified: false) };
        var streak = Run(changes, Day(8), freezes: 0);
        Assert.Equal(0, streak.Current);
        Assert.Empty(streak.Freezes);
    }

    [Fact]
    public void AFreezeNeedsAWaterChangeSoAMissedPeriodStaysBroken()
    {
        var streak = Run(Verified(0, 7, 30), Day(30));
        Assert.Equal(PeriodOutcome.Missed, streak.Periods[1].Outcome);
        Assert.Empty(streak.Freezes);
        Assert.Equal(2, streak.FreezesLeft);
    }

    [Fact]
    public void ThePeriodAfterAnUnverifiedWaterChangeStartsFromThatWaterChange()
    {
        var changes = new List<WaterChangeRecord> { Change(1, Day(0)), Change(2, Day(9), verified: false), Change(3, Day(19)) };
        var streak = Run(changes, Day(19));
        Assert.Equal(2, streak.Current);
        Assert.Equal(1, streak.CurrentVerified);
    }

    [Fact]
    public void TheAllotmentRenewsOnTheAnniversaryOfTheTankWhichDecidesByThePeriodEnd()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, new DateOnly(2027, 2, 20)), Change(2, new DateOnly(2027, 2, 25), verified: false),
            Change(3, new DateOnly(2027, 2, 28), verified: false), Change(4, new DateOnly(2027, 3, 3), verified: false),
            Change(5, new DateOnly(2027, 3, 6), verified: false), Change(6, new DateOnly(2027, 3, 9), verified: false),
        };
        var streak = Run(changes, new DateOnly(2027, 3, 9));
        Assert.Equal([1, 1, 2, 2], streak.Freezes.Select(f => f.TankYear));
        Assert.Equal([PeriodOutcome.Frozen, PeriodOutcome.Frozen, PeriodOutcome.Frozen, PeriodOutcome.Frozen, PeriodOutcome.Unverified],
            streak.Periods.Select(p => p.Outcome));
        Assert.Equal(0, streak.Current);
    }

    [Fact]
    public void TheCalendarYearDoesNotRenewTheAllotment()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, new DateOnly(2026, 12, 20)), Change(2, new DateOnly(2026, 12, 25), verified: false),
            Change(3, new DateOnly(2026, 12, 30), verified: false), Change(4, new DateOnly(2027, 1, 4), verified: false),
        };
        var streak = Run(changes, new DateOnly(2027, 1, 4));
        Assert.Equal([PeriodOutcome.Frozen, PeriodOutcome.Frozen, PeriodOutcome.Unverified], streak.Periods.Select(p => p.Outcome));
        Assert.Equal([1, 1], streak.Freezes.Select(f => f.TankYear));
    }

    [Fact]
    public void FreezesLeftCountsTheTankYearOfToday()
    {
        var changes = new List<WaterChangeRecord> { Change(1, new DateOnly(2027, 2, 20)), Change(2, new DateOnly(2027, 2, 25), verified: false) };
        Assert.Equal(1, Run(changes, new DateOnly(2027, 2, 28)).FreezesLeft);
        Assert.Equal(2, Run(changes, new DateOnly(2027, 3, 1)).FreezesLeft);
    }

    [Fact]
    public void WaterChangesBeforeTheTankWasCreatedBelongToTheFirstTankYear()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, Day(-20)), Change(2, Day(-15), verified: false), Change(3, Day(-10), verified: false), Change(4, Day(-5), verified: false),
        };
        var streak = Run(changes, Day(-5));
        Assert.Equal([1, 1], streak.Freezes.Select(f => f.TankYear));
        Assert.Equal(PeriodOutcome.Unverified, streak.Periods[2].Outcome);
    }

    [Theory]
    [InlineData(2026, 3, 1, 2026, 3, 1, 1)]
    [InlineData(2026, 3, 1, 2027, 2, 28, 1)]
    [InlineData(2026, 3, 1, 2027, 3, 1, 2)]
    [InlineData(2027, 3, 1, 2028, 2, 29, 1)]
    [InlineData(2027, 3, 1, 2028, 3, 1, 2)]
    [InlineData(2024, 2, 29, 2025, 2, 27, 1)]
    [InlineData(2024, 2, 29, 2025, 2, 28, 2)]
    [InlineData(2024, 2, 29, 2028, 2, 28, 4)]
    [InlineData(2024, 2, 29, 2028, 2, 29, 5)]
    [InlineData(2026, 3, 1, 2026, 2, 28, 1)]
    public void TheTankYearStartsOnTheCalendarAnniversaryOfTheCreationDate(int sy, int sm, int sd, int y, int m, int d, int expected) =>
        Assert.Equal(expected, WaterChangeStreaks.TankYear(new DateOnly(sy, sm, sd), new DateOnly(y, m, d)));

    [Fact]
    public void AWaterChangeDuringAVacationIsAnOrdinaryWaterChange()
    {
        var vacation = new VacationSpan(Day(2), Day(40));
        var streak = Run(Verified(0, 5, 9), Day(9), vacations: [vacation]);
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void AnActiveVacationPausesTheStreakWithoutADeadline()
    {
        var vacation = new VacationSpan(Day(10), null);
        var streak = Run(Verified(0, 7), Day(60), vacations: [vacation]);
        Assert.Equal(WaterChangeStreakState.Paused, streak.State);
        Assert.Equal(1, streak.Current);
        Assert.Null(streak.Deadline);
    }

    [Fact]
    public void AfterTheVacationEndsTheDeadlineIsTheGraceAfterTheLastVacationDay()
    {
        var vacation = new VacationSpan(Day(10), Day(59));
        var streak = Run(Verified(0, 7), Day(62), vacations: [vacation]);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
        Assert.Equal(1, streak.Current);
        Assert.Equal(Day(62), streak.Deadline);
    }

    [Fact]
    public void ARhythmChangeAppliesFromTheChangeOn()
    {
        var changes = Verified(0, 16);
        var changeAtDayFive = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 7, true), new(At(Day(5)), 14, true) };
        Assert.Equal(1, Run(changes, Day(16), rhythmChanges: changeAtDayFive).Current);
        var changeAtDay13 = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 7, true), new(At(Day(13)), 14, true) };
        var late = Run(changes, Day(16), rhythmChanges: changeAtDay13);
        Assert.Equal(PeriodOutcome.Missed, late.Periods.Single().Outcome);
    }

    [Fact]
    public void ARhythmChangeOnTheDayOfTheMissStillCounts()
    {
        var changeAtDay11 = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 7, true), new(At(Day(11)), 14, true) };
        Assert.Equal(PeriodOutcome.Verified, Run(Verified(0, 12), Day(12), rhythmChanges: changeAtDay11).Periods.Single().Outcome);
    }

    [Fact]
    public void AShorterRhythmAppliesToThePeriodThatIsRunning()
    {
        var shorter = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 14, true), new(At(Day(8)), 7, true) };
        Assert.Equal(PeriodOutcome.Missed, Run(Verified(0, 12), Day(12), rhythmChanges: shorter).Periods.Single().Outcome);
        Assert.Equal(PeriodOutcome.Verified, Run(Verified(0, 10), Day(10), rhythmChanges: shorter).Periods.Single().Outcome);
    }

    [Fact]
    public void TheDeadlineOfTheRunningPeriodUsesTheRhythmOfToday()
    {
        var changes = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 7, true), new(At(Day(3)), 14, true) };
        Assert.Equal(Day(19), Run(Verified(0), Day(5), rhythmChanges: changes).Deadline);
    }

    [Fact]
    public void WithoutStoredChangesTheDefaultRhythmOfSevenDaysApplies()
    {
        var streak = WaterChangeStreaks.Evaluate(Verified(0, 11), [], [], new HashSet<long>(), 2, Start, Day(11), 0);
        Assert.Equal(PeriodOutcome.Missed, streak.Periods.Single().Outcome);
    }

    [Fact]
    public void ADisabledRulePausesTheStreak()
    {
        var changes = new List<RhythmChange> { new(DateTimeOffset.UnixEpoch, 7, true), new(At(Day(3)), 7, false), new(At(Day(40)), 7, true) };
        var streak = Run(Verified(0, 42), Day(42), rhythmChanges: changes);
        Assert.Equal(PeriodOutcome.Bridged, streak.Periods.Single().Outcome);
        Assert.Equal(WaterChangeStreakState.Running, streak.State);
    }

    [Fact]
    public void TwoWaterChangesOnTheSameLocalDayCountAsOneAndTheVerifiedOneWins()
    {
        var changes = new List<WaterChangeRecord>
        {
            Change(1, Day(0)), Change(2, Day(7), verified: false, hour: 8), Change(3, Day(7), hour: 18), Change(4, Day(14)),
        };
        var streak = Run(changes, Day(14));
        Assert.Equal(2, streak.Current);
        Assert.Equal(2, streak.CurrentVerified);
        Assert.Empty(streak.Freezes);
    }

    [Fact]
    public void WaterChangesAreSortedByTimeWhateverTheOrderOfTheInput()
    {
        var streak = Run([Change(3, Day(14)), Change(1, Day(0)), Change(2, Day(7))], Day(14));
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void TheLocalDayOfTheMemberDecidesWhichDayAWaterChangeFallsOn()
    {
        var first = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 3, 11, 23, 0, 0, TimeSpan.Zero);
        var changes = new List<WaterChangeRecord> { new(1, first, true), new(2, second, true) };
        Assert.Equal(PeriodOutcome.Verified, Run(changes, Day(13), utcOffsetMinutes: 0).Periods.Single().Outcome);
        Assert.Equal(PeriodOutcome.Missed, Run(changes, Day(13), utcOffsetMinutes: 120).Periods.Single().Outcome);
        Assert.Equal(PeriodOutcome.Verified, Run(changes, Day(13), utcOffsetMinutes: -300).Periods.Single().Outcome);
    }

    [Fact]
    public void TheLastWaterChangeAndTheDeadlineFollowTheLocalDay()
    {
        var late = new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero);
        var streak = Run([new WaterChangeRecord(1, late, true)], Day(2), utcOffsetMinutes: 120);
        Assert.Equal(Day(1), streak.LastWaterChange);
        Assert.Equal(Day(11), streak.Deadline);
    }

    private static DateTimeOffset At(DateOnly date) => new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
}
