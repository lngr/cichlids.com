using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Streaks;

public sealed class VacationSpanTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void ASpanCoversItsBoundsAndAnOpenSpanEverythingFromItsStart()
    {
        var closed = new VacationSpan(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));
        Assert.False(Vacations.Covers([closed], new DateOnly(2026, 9, 30)));
        Assert.True(Vacations.Covers([closed], new DateOnly(2026, 10, 1)));
        Assert.True(Vacations.Covers([closed], new DateOnly(2026, 10, 3)));
        Assert.False(Vacations.Covers([closed], new DateOnly(2026, 10, 4)));
        var open = new VacationSpan(new DateOnly(2026, 10, 10), null);
        Assert.True(Vacations.Covers([open], new DateOnly(2027, 1, 1)));
        Assert.False(Vacations.Covers([open], new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public void PastVacationsAreTheEndedOnes()
    {
        var spans = new List<VacationSpan>
        {
            new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)), new(new DateOnly(2026, 10, 10), null),
            new(new DateOnly(2026, 10, 12), Today),
        };
        Assert.Equal([spans[0], spans[2]], Vacations.Past(spans));
    }

    [Fact]
    public void TheShareIsTheVacationDaysOfThePastWindowOverTheWindow()
    {
        var spans = new List<VacationSpan> { new(Today.AddDays(-40), Today.AddDays(-31)), new(Today.AddDays(-10), Today.AddDays(-6)) };
        var share = Vacations.Share(spans, Today, windowDays: 100, firstDay: Today.AddDays(-500));
        Assert.Equal(15, share.VacationDays);
        Assert.Equal(100, share.WindowDays);
        Assert.Equal(0.15, share.Fraction, 3);
    }

    [Fact]
    public void TheWindowStartsAtTheFirstDayOfAYoungerMemberAndClipsVacationsAtItsEdges()
    {
        var spans = new List<VacationSpan> { new(Today.AddDays(-30), Today.AddDays(-20)), new(Today.AddDays(-400), Today.AddDays(-355)) };
        var share = Vacations.Share(spans, Today, windowDays: 365, firstDay: Today.AddDays(-49));
        Assert.Equal(50, share.WindowDays);
        Assert.Equal(11, share.VacationDays);
        var clipped = Vacations.Share(spans, Today, windowDays: 365, firstDay: Today.AddDays(-1000));
        Assert.Equal(365, clipped.WindowDays);
        Assert.Equal(11 + 10, clipped.VacationDays);
    }

    [Fact]
    public void OverlappingSpansCountEachDayOnce()
    {
        var spans = new List<VacationSpan> { new(Today.AddDays(-20), Today.AddDays(-10)), new(Today.AddDays(-15), Today.AddDays(-5)) };
        Assert.Equal(16, Vacations.Share(spans, Today, 100, Today.AddDays(-100)).VacationDays);
    }

    [Fact]
    public void ActiveSpansAreNotPartOfThePastShare()
    {
        var spans = new List<VacationSpan> { new(Today.AddDays(-3), null) };
        var share = Vacations.Share(spans, Today, 100, Today.AddDays(-100));
        Assert.Equal(0, share.VacationDays);
    }
}
