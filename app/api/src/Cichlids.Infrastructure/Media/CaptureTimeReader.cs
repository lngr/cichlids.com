using System.Globalization;
using System.Text.RegularExpressions;
using NetVips;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// How a read of the capture time ended. Found: a plausible time was read. NoDate: the image has
/// no original capture date. Implausible: the date is the zero date, malformed, before 1990 or
/// more than a day in the future. Unreadable: the bytes are not an image of an accepted format.
/// </summary>
public enum CaptureTimeOutcome
{
    Found,
    NoDate,
    Implausible,
    Unreadable,
}

/// <summary>
/// The capture time of a photo as the camera recorded it: the wall-clock time (DateTimeKind
/// Unspecified) and, when the camera wrote an offset, the offset from UTC. A null offset means the
/// wall-clock time has no known zone and is not UTC.
/// </summary>
public sealed record CaptureTime(DateTime LocalTime, TimeSpan? Offset);

/// <summary>
/// The outcome of reading a capture time together with the time when one was found.
/// </summary>
public sealed record CaptureTimeReading(CaptureTimeOutcome Outcome, CaptureTime? Time);

/// <summary>
/// Reads the capture time (EXIF DateTimeOriginal with OffsetTimeOriginal) from the EXIF block of an
/// image, through the libvips header loaders of the accepted formats. Only the header is parsed,
/// so the first HeaderBytes bytes of a file are enough. No other EXIF field, in particular no
/// location, is read.
/// </summary>
public static partial class CaptureTimeReader
{
    /// <summary>
    /// The number of leading file bytes that hold the EXIF block of practically every photo.
    /// </summary>
    public const int HeaderBytes = 262_144;

    private const string DateField = "exif-ifd2-DateTimeOriginal";
    private const string OffsetField = "exif-ifd2-OffsetTimeOriginal";

    private static readonly DateTime EarliestPlausible = new(1990, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Reads the capture time from the image bytes. A time later than one day after now is
    /// implausible.
    /// </summary>
    public static CaptureTimeReading Read(ReadOnlySpan<byte> bytes, DateTimeOffset now)
    {
        if (bytes.IsEmpty)
        {
            return new CaptureTimeReading(CaptureTimeOutcome.Unreadable, null);
        }

        ImageProcessor.EnsureLibvipsConfigured();
        string? date;
        string? offset;
        try
        {
            using var image = Image.NewFromBuffer(bytes.ToArray(), access: Enums.Access.Sequential);
            date = ReadField(image, DateField);
            offset = ReadField(image, OffsetField);
        }
        catch (VipsException)
        {
            return new CaptureTimeReading(CaptureTimeOutcome.Unreadable, null);
        }

        if (date is null)
        {
            return new CaptureTimeReading(CaptureTimeOutcome.NoDate, null);
        }

        if (!DateTime.TryParseExact(
                date, "yyyy':'MM':'dd HH':'mm':'ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || local < EarliestPlausible
            || local > now.UtcDateTime.AddDays(1))
        {
            return new CaptureTimeReading(CaptureTimeOutcome.Implausible, null);
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new CaptureTimeReading(CaptureTimeOutcome.Found, new CaptureTime(local, ParseOffset(offset)));
    }

    // libvips renders an EXIF value as "value (value, type, components, bytes)".
    private static string? ReadField(Image image, string name)
    {
        if (image.GetTypeOf(name) == 0)
        {
            return null;
        }

        var text = image.Get(name)?.ToString();
        if (text is null)
        {
            return null;
        }

        var end = text.IndexOf(" (", StringComparison.Ordinal);
        var value = (end < 0 ? text : text[..end]).Trim();
        return value.Length == 0 ? null : value;
    }

    private static TimeSpan? ParseOffset(string? text)
    {
        var match = text is null ? null : OffsetPattern().Match(text);
        if (match is not { Success: true })
        {
            return null;
        }

        var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (hours > 14 || minutes > 59)
        {
            return null;
        }

        var span = new TimeSpan(hours, minutes, 0);
        return match.Groups["s"].Value == "-" ? -span : span;
    }

    [GeneratedRegex(@"^(?<s>[+-])(?<h>\d{2}):(?<m>\d{2})$")]
    private static partial Regex OffsetPattern();
}
