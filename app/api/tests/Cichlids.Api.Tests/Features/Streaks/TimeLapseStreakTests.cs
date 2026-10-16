using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

public sealed class TimeLapseStreakTests
{
    // Monday of an ISO week.
    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static SeriesPhoto Photo(long id, DateOnly date, int hour = 12, int minute = 0) =>
        new(id, new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero));

    private static TimeLapseStreak Run(IReadOnlyList<SeriesPhoto> photos, DateOnly today, IReadOnlyList<VacationSpan>? vacations = null,
        int utcOffsetMinutes = 0) => TimeLapseStreaks.Evaluate(photos, utcOffsetMinutes, vacations ?? [], today);

    private static List<SeriesPhoto> WeeklyPhotos(params int[] weekOffsets) =>
        weekOffsets.Select((week, index) => Photo(index + 1, Monday.AddDays(week * 7 + 2))).ToList();

    [Fact]
    public void WithoutPhotosThereIsNoStreakAndTheWeekIsOpen()
    {
        var streak = Run([], Monday.AddDays(2));
        Assert.Equal(0, streak.Current);
        Assert.Equal(0, streak.Longest);
        Assert.False(streak.ThisWeekHasPhoto);
        Assert.True(streak.ThisWeekOpen);
        Assert.Equal(Monday, streak.WeekStart);
        Assert.Equal(Monday.AddDays(6), streak.WeekEnd);
    }

    [Fact]
    public void ThreeWeeksInARowCountThreeAndTheWeekWithAPhotoIsClosed()
    {
        var streak = Run(WeeklyPhotos(-2, -1, 0), Monday.AddDays(3));
        Assert.Equal(3, streak.Current);
        Assert.Equal(3, streak.Longest);
        Assert.True(streak.ThisWeekHasPhoto);
        Assert.False(streak.ThisWeekOpen);
    }

    [Fact]
    public void TheCurrentWeekWithoutAPhotoIsStillOpenAndKeepsTheStreak()
    {
        var streak = Run(WeeklyPhotos(-3, -2, -1), Monday.AddDays(6));
        Assert.Equal(3, streak.Current);
        Assert.True(streak.ThisWeekOpen);
        Assert.False(streak.ThisWeekHasPhoto);
    }

    [Fact]
    public void AWeekWithoutAPhotoBreaksTheStreakAndANewPhotoStartsAgain()
    {
        var broken = Run(WeeklyPhotos(-5, -4, -3, -1), Monday.AddDays(1));
        Assert.Equal(1, broken.Current);
        Assert.Equal(3, broken.Longest);
        var dead = Run(WeeklyPhotos(-5, -4, -3), Monday.AddDays(1));
        Assert.Equal(0, dead.Current);
        Assert.Equal(3, dead.Longest);
        Assert.True(dead.ThisWeekOpen);
    }

    [Fact]
    public void SeveralPhotosInOneWeekCountOnce()
    {
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-7)), Photo(2, Monday.AddDays(-6)), Photo(3, Monday.AddDays(-2)) };
        Assert.Equal(1, Run(photos, Monday.AddDays(1)).Current);
    }

    [Fact]
    public void ISOWeeksStartOnMondayAndSundayBelongsToTheWeekBefore()
    {
        var sunday = Photo(1, Monday.AddDays(-1), 23, 59);
        var mondayPhoto = Photo(2, Monday, 0, 1);
        var streak = Run([sunday, mondayPhoto], Monday.AddDays(2));
        Assert.Equal(2, streak.Current);
        Assert.Equal(Monday, TimeLapseStreaks.WeekStartOf(Monday.AddDays(6)));
        Assert.Equal(Monday, TimeLapseStreaks.WeekStartOf(Monday));
        Assert.Equal(Monday.AddDays(-7), TimeLapseStreaks.WeekStartOf(Monday.AddDays(-1)));
    }

    [Fact]
    public void TheYearBoundaryLinksWeek53OfOneYearToWeek1OfTheNext()
    {
        var week53 = Photo(1, new DateOnly(2026, 12, 30));
        var week1 = Photo(2, new DateOnly(2027, 1, 4));
        var streak = Run([week53, week1], new DateOnly(2027, 1, 5));
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void TheMembersOffsetMovesAPhotoIntoTheNextWeek()
    {
        var sundayNight = new DateTimeOffset(2026, 9, 13, 23, 30, 0, TimeSpan.Zero);
        var photos = new List<SeriesPhoto> { new(1, sundayNight) };
        var utc = Run(photos, Monday.AddDays(7), utcOffsetMinutes: 0);
        Assert.Equal(Monday.AddDays(7), utc.WeekStart);
        Assert.False(utc.ThisWeekHasPhoto);
        var ahead = Run(photos, Monday.AddDays(7), utcOffsetMinutes: 120);
        Assert.True(ahead.ThisWeekHasPhoto);
        var early = new List<SeriesPhoto> { new(2, new DateTimeOffset(2026, 9, 14, 1, 0, 0, TimeSpan.Zero)) };
        Assert.True(Run(early, Monday.AddDays(7), utcOffsetMinutes: 0).ThisWeekHasPhoto);
        Assert.False(Run(early, Monday.AddDays(7), utcOffsetMinutes: -120).ThisWeekHasPhoto);
    }

    [Fact]
    public void AVacationWeekWithoutAPhotoIsNeutral()
    {
        var vacation = new VacationSpan(Monday.AddDays(-14), Monday.AddDays(-9));
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-20)), Photo(2, Monday.AddDays(-5)) };
        var streak = Run(photos, Monday.AddDays(1), [vacation]);
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void AVacationWeekNeverExtendsTheStreakAndAPartialVacationWeekBridgesOnly()
    {
        var vacation = new VacationSpan(Monday.AddDays(-35), Monday.AddDays(-9));
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-40)), Photo(2, Monday.AddDays(-5)) };
        var streak = Run(photos, Monday.AddDays(1), [vacation]);
        Assert.Equal(2, streak.Current);
        Assert.Equal(2, streak.Longest);
    }

    [Fact]
    public void AWeekTouchedByAVacationDayWithoutAPhotoDoesNotBreak()
    {
        var vacation = new VacationSpan(Monday.AddDays(-4), Monday.AddDays(-4));
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-12)) };
        var streak = Run(photos, Monday.AddDays(1), [vacation]);
        Assert.Equal(1, streak.Current);
    }

    [Fact]
    public void AnActiveVacationPausesTheWeekWithoutAnOpenDeadline()
    {
        var vacation = new VacationSpan(Monday.AddDays(-30), null);
        var streak = Run(WeeklyPhotos(-6, -5), Monday.AddDays(3), [vacation]);
        Assert.True(streak.Paused);
        Assert.False(streak.ThisWeekOpen);
        Assert.Equal(2, streak.Current);
    }

    [Fact]
    public void AVacationThatEndedBridgesTheWeeksItTouches()
    {
        var vacation = new VacationSpan(Monday.AddDays(-14), Monday.AddDays(-4));
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-20)) };
        Assert.Equal(0, Run(photos, Monday.AddDays(8)).Current);
        Assert.Equal(1, Run(photos, Monday.AddDays(1), [vacation]).Current);
    }

    [Fact]
    public void OnlyTheSolePhotoOfAWeekOfTheCurrentRunKeepsTheStreak()
    {
        var photos = new List<SeriesPhoto>
        {
            Photo(1, Monday.AddDays(-20)), Photo(2, Monday.AddDays(-12)), Photo(3, Monday.AddDays(-11)), Photo(4, Monday.AddDays(-5)),
        };
        var streak = Run(photos, Monday.AddDays(1));
        Assert.Equal(3, streak.Current);
        Assert.Equal([1L, 4L], streak.KeepingPhotoIds.OrderBy(i => i));
    }

    [Fact]
    public void PhotosOfABrokenRunDoNotKeepTheCurrentStreak()
    {
        var photos = new List<SeriesPhoto> { Photo(1, Monday.AddDays(-30)), Photo(2, Monday.AddDays(-5)) };
        var streak = Run(photos, Monday.AddDays(1));
        Assert.Equal([2L], streak.KeepingPhotoIds);
    }

    [Fact]
    public void LongestIsTheBestRunEvenWhenItEndedLongAgo()
    {
        var streak = Run(WeeklyPhotos(-20, -19, -18, -17, -10, -1), Monday.AddDays(2));
        Assert.Equal(1, streak.Current);
        Assert.Equal(4, streak.Longest);
    }
}
