using System.Text.RegularExpressions;

namespace Cichlids.Api.Features.WebPush;

/// <summary>Looks up the IANA time zones that browsers report.</summary>
public static partial class TimeZones
{
    [GeneratedRegex("^[A-Za-z0-9_+\\-]+(/[A-Za-z0-9_+\\-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ZoneId();

    /// <summary>The zone of an IANA name such as Europe/Berlin; false for a name that is not a known zone.</summary>
    public static bool TryFind(string? name, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        return name is { Length: > 0 and <= 64 } && ZoneId().IsMatch(name) && TimeZoneInfo.TryFindSystemTimeZoneById(name, out zone!);
    }
}
