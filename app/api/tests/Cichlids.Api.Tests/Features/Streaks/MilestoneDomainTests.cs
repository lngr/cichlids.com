using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

public sealed class MilestoneDomainTests
{
    private static readonly DateOnly Monday = new(2026, 6, 1);

    private static SeriesPhoto Photo(long id, DateOnly date) =>
        new(id, new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, TimeSpan.Zero));

    private static List<SeriesPhoto> Weeks(params int[] weekOffsets) =>
        weekOffsets.Select((week, index) => Photo(index + 1, Monday.AddDays(week * 7 + 2))).ToList();

    private static int[] Thresholds(IReadOnlyDictionary<int, DateOnly> reached) => reached.Keys.Order().ToArray();

    [Fact]
    public void AStreakOfFourWeeksIsReachedOnTheDayOfThePhotoOfTheFourthWeek()
    {
        var reached = TimeLapseStreaks.ReachDates(Weeks(0, 1, 2, 3), 0, [], Monday.AddDays(30), Milestones.StreakWeeks);
        Assert.Equal(new[] { 4 }, Thresholds(reached));
        Assert.Equal(Monday.AddDays(3 * 7 + 2), reached[4]);
    }

    [Fact]
    public void ThreeWeeksReachNoThreshold()
    {
        Assert.Empty(TimeLapseStreaks.ReachDates(Weeks(0, 1, 2), 0, [], Monday.AddDays(30), Milestones.StreakWeeks));
    }

    [Fact]
    public void AMissedWeekRestartsTheCountAndAVacationWeekBridgesIt()
    {
        var missed = TimeLapseStreaks.ReachDates(Weeks(0, 1, 2, 4, 5), 0, [], Monday.AddDays(60), Milestones.StreakWeeks);
        Assert.Empty(missed);
        var bridged = TimeLapseStreaks.ReachDates(Weeks(0, 1, 2, 4), 0,
            [new VacationSpan(Monday.AddDays(3 * 7), Monday.AddDays(3 * 7 + 3))], Monday.AddDays(60), Milestones.StreakWeeks);
        Assert.Equal(Monday.AddDays(4 * 7 + 2), bridged[4]);
    }

    [Fact]
    public void ARunReachesEveryThresholdItPasses()
    {
        var reached = TimeLapseStreaks.ReachDates(Weeks(Enumerable.Range(0, 13).ToArray()), 0, [], Monday.AddDays(120), Milestones.StreakWeeks);
        Assert.Equal(new[] { 4, 12 }, Thresholds(reached));
    }

    [Fact]
    public void PhotosAfterTodayAreIgnored()
    {
        Assert.Empty(TimeLapseStreaks.ReachDates(Weeks(0, 1, 2, 3), 0, [], Monday.AddDays(3 * 7), Milestones.StreakWeeks));
    }

    [Fact]
    public void AMilestoneIsVisibleOnTheDayItIsReachedAndForSixMoreDays()
    {
        var reached = new DateOnly(2026, 10, 10);
        Assert.False(Milestones.IsVisible(reached, reached.AddDays(-1)));
        Assert.True(Milestones.IsVisible(reached, reached));
        Assert.True(Milestones.IsVisible(reached, reached.AddDays(6)));
        Assert.False(Milestones.IsVisible(reached, reached.AddDays(7)));
    }

    [Fact]
    public void TheKeyNamesTheKindTheTankAndTheValue()
    {
        Assert.Equal("kept_streak:7:12", Milestones.Key(MilestoneKind.KeptStreak, 7, 12));
        Assert.Equal("tank_anniversary:7:2", Milestones.Key(MilestoneKind.TankAnniversary, 7, 2));
        Assert.Equal("first_care_week:7", Milestones.Key(MilestoneKind.FirstCareWeek, 7, 0));
    }

    [Theory]
    [InlineData("kept_streak:7:12", MilestoneKind.KeptStreak, 7L, 12)]
    [InlineData("tank_anniversary:7:3", MilestoneKind.TankAnniversary, 7L, 3)]
    [InlineData("first_care_week:7", MilestoneKind.FirstCareWeek, 7L, 0)]
    public void AKeyIsParsedBackToItsParts(string key, MilestoneKind kind, long tank, int value)
    {
        var parsed = Milestones.TryParseKey(key);
        Assert.NotNull(parsed);
        Assert.Equal(kind, parsed.Value.Kind);
        Assert.Equal(tank, parsed.Value.SubjectId);
        Assert.Equal(value, parsed.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kept_streak:7")]
    [InlineData("kept_streak:7:5")]
    [InlineData("kept_streak:x:4")]
    [InlineData("tank_anniversary:7:0")]
    [InlineData("first_care_week:7:1")]
    [InlineData("other:7:4")]
    [InlineData("kept_streak:-1:4")]
    public void AnUnknownKeyIsRefused(string key) => Assert.Null(Milestones.TryParseKey(key));

    [Fact]
    public void ATankAnniversaryFallsOnTheCalendarAnniversary()
    {
        var start = new DateOnly(2024, 3, 10);
        var years = Milestones.Anniversaries(start, [], new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 14));
        Assert.Equal((2, new DateOnly(2026, 3, 10)), Assert.Single(years));
        Assert.Empty(Milestones.Anniversaries(start, [], new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 17)));
    }

    [Fact]
    public void ADecommissionedStretchShiftsTheAnniversary()
    {
        var start = new DateOnly(2024, 3, 10);
        var spans = new[] { new DecommissionSpan(new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 11)) };
        Assert.Empty(Milestones.Anniversaries(start, spans, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 19)));
        Assert.Equal((1, new DateOnly(2025, 3, 20)), Assert.Single(Milestones.Anniversaries(start, spans, new DateOnly(2025, 3, 15), new DateOnly(2025, 3, 21))));
    }

    [Fact]
    public void TheFirstDayOfATankIsNoAnniversary()
    {
        var start = new DateOnly(2026, 3, 10);
        Assert.Empty(Milestones.Anniversaries(start, [], start, start.AddDays(6)));
    }

    [Fact]
    public void TheFirstCareWeekIsTheClosingDayOfTheFirstPeriodThatMetThePlan()
    {
        var periods = new[]
        {
            new WaterChangePeriod(1, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), PeriodOutcome.Missed),
            new WaterChangePeriod(2, new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 8), PeriodOutcome.Bridged),
            new WaterChangePeriod(3, new DateOnly(2026, 7, 8), new DateOnly(2026, 7, 15), PeriodOutcome.Frozen),
            new WaterChangePeriod(4, new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 22), PeriodOutcome.Verified),
        };
        Assert.Equal(new DateOnly(2026, 7, 15), Milestones.FirstCareWeek(periods));
        Assert.Null(Milestones.FirstCareWeek(periods.Take(2).ToList()));
    }
}
