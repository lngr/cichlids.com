using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Discover;

public static class DiscoverRanking
{
    public const int MinimumRatings = 10;
    public static double Score(long sum, int count, double prior) => (sum + 10 * prior) / (count + 10);

    public static bool InRegion(TankRegion? actual, TankRegion selected) => actual == selected
        || actual == TankRegion.AmericasUnassigned && selected is TankRegion.CentralAmerica or TankRegion.SouthAmerica;

    public static int WeeklyIndex(DateTimeOffset now, int count)
    {
        if (count == 0) return 0;
        var date = now.UtcDateTime.Date;
        var monday = date.AddDays(-((int)date.DayOfWeek + 6) % 7);
        var weeks = (int)((monday - new DateTime(2000, 1, 3)).TotalDays / 7);
        return (weeks % count + count) % count;
    }
}
