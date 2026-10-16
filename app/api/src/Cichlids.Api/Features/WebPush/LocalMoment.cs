namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// An instant read on the clock of an IANA time zone: the local calendar date, the local time of
/// day and the offset of the clock from UTC in minutes at that instant.
/// </summary>
public readonly record struct LocalMoment(DateOnly Date, TimeSpan TimeOfDay, int UtcOffsetMinutes)
{
    /// <summary>Reads the instant on the clock of the zone.</summary>
    public static LocalMoment Of(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new LocalMoment(DateOnly.FromDateTime(local.DateTime), local.TimeOfDay, (int)local.Offset.TotalMinutes);
    }
}
