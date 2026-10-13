// Story: task-3.32
using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

public sealed class LegacyLocalTimeTests
{
    [Theory]
    [InlineData("2016-01-15 12:00:00", "2016-01-15 17:00:00")]
    [InlineData("2016-07-15 12:00:00", "2016-07-15 16:00:00")]
    [InlineData("2016-03-13 01:59:59", "2016-03-13 06:59:59")]
    [InlineData("2016-03-13 03:00:00", "2016-03-13 07:00:00")]
    [InlineData("2016-11-06 00:59:59", "2016-11-06 04:59:59")]
    [InlineData("2016-11-06 02:00:00", "2016-11-06 07:00:00")]
    public void ConvertsNewYorkLocalTimeToUtc(string local, string expectedUtc)
    {
        AssertUtc(expectedUtc, LegacyLocalTime.ToUtc(Parse(local)));
    }

    [Fact]
    public void ShiftsATimeInTheSpringGapOneHourForward()
    {
        // 02:30 does not exist on 2016-03-13; read one hour later it is 03:30 daylight time.
        AssertUtc("2016-03-13 07:30:00", LegacyLocalTime.ToUtc(Parse("2016-03-13 02:30:00")));
    }

    [Fact]
    public void ReadsAnAmbiguousAutumnTimeAsStandardTime()
    {
        // 01:30 occurs twice on 2016-11-06; standard time is UTC-5.
        AssertUtc("2016-11-06 06:30:00", LegacyLocalTime.ToUtc(Parse("2016-11-06 01:30:00")));
    }

    private static DateTime Parse(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd HH:mm:ss", null);

    private static void AssertUtc(string expected, DateTimeOffset actual)
    {
        Assert.Equal(TimeSpan.Zero, actual.Offset);
        Assert.Equal(Parse(expected), actual.UtcDateTime);
    }
}
