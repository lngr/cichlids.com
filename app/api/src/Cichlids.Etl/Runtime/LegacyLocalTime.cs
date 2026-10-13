namespace Cichlids.Etl.Runtime;

/// <summary>
/// Converts a legacy DATETIME value to UTC. The legacy host ran on US Eastern time and wrote these
/// values with the local clock, so a value is a wall-clock time in America/New_York with daylight
/// saving. A time that falls into the spring gap is read one hour later, and a time the autumn
/// change makes ambiguous is read as standard time.
/// </summary>
public static class LegacyLocalTime
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static DateTimeOffset ToUtc(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (NewYork.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddHours(1);
        }

        var offset = NewYork.IsAmbiguousTime(wallClock) ? NewYork.BaseUtcOffset : NewYork.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset).ToUniversalTime();
    }
}
