namespace Cichlids.Domain;

/// <summary>
/// Judges the photo that proves a decommissioning or the end of one. The photo arrives in the request
/// of the action, so the server receives it during the action; its capture time has to lie close to
/// that moment.
/// </summary>
public static class DecommissionProofs
{
    public const string NotInApp = "photo_not_in_app";
    public const string TimeImplausible = "photo_time_implausible";
    public const string Reused = "photo_reused";

    // A capture time without an offset is a wall-clock time of an unknown zone, which can differ
    // from UTC by up to this much.
    private static readonly TimeSpan WidestZoneSpread = TimeSpan.FromHours(14);

    /// <summary>
    /// The finding that disqualifies the photo, or null when it qualifies: the client has to report
    /// the camera of the app as the source, the EXIF capture time (when present) has to lie within the
    /// clock tolerance of the moment the server received the photo, and the same picture has not
    /// served as proof before. A capture time without a known offset gets the widest zone spread on top
    /// of the tolerance.
    /// </summary>
    public static string? Check(EvidencePhoto photo, TimeSpan clockTolerance)
    {
        if (!photo.CapturedInApp) return NotInApp;
        if (photo.TakenAt is { } taken)
        {
            var tolerance = photo.OffsetKnown ? clockTolerance : clockTolerance + WidestZoneSpread;
            if ((taken - photo.ReceivedAt).Duration() > tolerance) return TimeImplausible;
        }
        return photo.ChecksumUsedBefore ? Reused : null;
    }
}
