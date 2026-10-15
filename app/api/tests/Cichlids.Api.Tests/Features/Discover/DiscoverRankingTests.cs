using Cichlids.Api.Features.Discover;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Discover;

public class DiscoverRankingTests
{
    [Fact]
    public void BayesianScore_UsesTenVotesOfPriorWeight()
    {
        Assert.Equal(4.0, DiscoverRanking.Score(50, 10, 3), 10);
        Assert.Equal(3.0, DiscoverRanking.Score(0, 0, 3), 10);
        Assert.True(DiscoverRanking.Score(490, 100, 3) > DiscoverRanking.Score(50, 10, 3));
        Assert.Equal(10, DiscoverRanking.MinimumRatings);
    }

    [Theory]
    [InlineData(TankRegion.CentralAmerica, true)]
    [InlineData(TankRegion.SouthAmerica, true)]
    [InlineData(TankRegion.LakeMalawi, false)]
    [InlineData(TankRegion.Mixed, false)]
    public void UnassignedAmericas_AppliesToBothAmericanRegions(TankRegion selected, bool expected) =>
        Assert.Equal(expected, DiscoverRanking.InRegion(TankRegion.AmericasUnassigned, selected));

    [Fact]
    public void Rotation_ChangesOnIsoMondayIncludingYearBoundaryAndWraps()
    {
        var sunday = DateTimeOffset.Parse("2027-01-03T23:59:59Z");
        Assert.Equal(DiscoverRanking.WeeklyIndex(sunday.AddDays(-6), 20), DiscoverRanking.WeeklyIndex(sunday, 20));
        Assert.Equal((DiscoverRanking.WeeklyIndex(sunday, 20) + 1) % 20, DiscoverRanking.WeeklyIndex(sunday.AddSeconds(1), 20));
        Assert.Equal(DiscoverRanking.WeeklyIndex(sunday, 20), DiscoverRanking.WeeklyIndex(sunday.AddDays(140), 20));
        Assert.Equal(0, DiscoverRanking.WeeklyIndex(sunday, 0));
        Assert.Equal(0, DiscoverRanking.WeeklyIndex(sunday, 1));
    }
}
