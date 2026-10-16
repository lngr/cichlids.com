using Cichlids.Domain;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Care;

public sealed class CareHeatmapTests
{
    // A Thursday; its ISO week starts on Monday 2026-10-12.
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static CareDayKindCount Row(DateOnly date, CareKind kind, int count = 1) => new(date, kind, count);

    [Fact]
    public void TheWindowRunsFromTheMondayFifteenWeeksBeforeThisWeekUpToToday()
    {
        var map = CareHeatmaps.Build([], Today);
        Assert.Equal(new DateOnly(2026, 6, 29), map.Start);
        Assert.Equal(DayOfWeek.Monday, map.Start.DayOfWeek);
        Assert.Equal(Today, map.End);
        Assert.Equal(16, CareHeatmaps.Weeks);
        Assert.Equal(15 * 7, TimeLapseStreaks.WeekStartOf(Today).DayNumber - map.Start.DayNumber);
    }

    [Fact]
    public void OnASundayTheWindowEndsWithTheFullLastWeek()
    {
        var sunday = new DateOnly(2026, 10, 18);
        var map = CareHeatmaps.Build([], sunday);
        Assert.Equal(new DateOnly(2026, 6, 29), map.Start);
        Assert.Equal(sunday, map.End);
    }

    [Fact]
    public void OnAMondayTheLastWeekHasOneDay()
    {
        var monday = new DateOnly(2026, 10, 12);
        var map = CareHeatmaps.Build([], monday);
        Assert.Equal(new DateOnly(2026, 6, 29), map.Start);
        Assert.Equal(monday, map.End);
    }

    [Fact]
    public void RowsOfTheSameDayAndKindAreSummedAndKindsAreSeparate()
    {
        var map = CareHeatmaps.Build([
            Row(Today, CareKind.WaterChange, 2), Row(Today, CareKind.WaterChange), Row(Today, CareKind.Measurement, 4),
            Row(Today.AddDays(-1), CareKind.Measurement)], Today);
        Assert.Equal(2, map.Days.Count);
        Assert.Equal(new CareDayCount(Today.AddDays(-1), 0, 1), map.Days[0]);
        Assert.Equal(new CareDayCount(Today, 3, 4), map.Days[1]);
        Assert.Equal(2, map.ActiveDays);
    }

    [Fact]
    public void DaysOutsideTheWindowAreDropped()
    {
        var map = CareHeatmaps.Build([
            Row(WindowStart().AddDays(-1), CareKind.WaterChange), Row(WindowStart(), CareKind.WaterChange),
            Row(Today, CareKind.Measurement), Row(Today.AddDays(1), CareKind.Measurement)], Today);
        Assert.Equal([WindowStart(), Today], map.Days.Select(d => d.Date));
    }

    private static DateOnly WindowStart() => CareHeatmaps.Build([], Today).Start;

    [Fact]
    public void TheLatestDaysPerKindAreReported()
    {
        var map = CareHeatmaps.Build([
            Row(Today.AddDays(-20), CareKind.WaterChange), Row(Today.AddDays(-9), CareKind.WaterChange),
            Row(Today.AddDays(-2), CareKind.Measurement)], Today);
        Assert.Equal(Today.AddDays(-9), map.LastWaterChange);
        Assert.Equal(Today.AddDays(-2), map.LastMeasurement);
        Assert.Null(CareHeatmaps.Build([], Today).LastWaterChange);
    }

    [Fact]
    public void TheQueryRangeCoversExactlyTheLocalDaysOfTheWindow()
    {
        var (from, to) = CareHeatmaps.QueryRange(Today, 120);
        Assert.Equal(new DateTimeOffset(2026, 6, 28, 22, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 22, 0, 0, TimeSpan.Zero), to);
        var (westFrom, westTo) = CareHeatmaps.QueryRange(Today, -300);
        Assert.Equal(new DateTimeOffset(2026, 6, 29, 5, 0, 0, TimeSpan.Zero), westFrom);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 5, 0, 0, TimeSpan.Zero), westTo);
    }
}
