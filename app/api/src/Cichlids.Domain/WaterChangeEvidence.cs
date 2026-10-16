namespace Cichlids.Domain;

/// <summary>
/// How well the guided mode proves a water change: Verified when the mode ran with enough time
/// between the start of draining and the end and at least one photo was taken with the camera of
/// the app, Partial when it was finished but too short, without the draining step or without such
/// a photo, None for an entry logged without the mode.
/// </summary>
public enum WaterChangeEvidenceState { None, Partial, Verified }

/// <summary>
/// The evidence state of a water change and the findings that keep it from being verified, as
/// codes in a fixed order.
/// </summary>
public sealed record WaterChangeEvidence(WaterChangeEvidenceState State, IReadOnlyList<string> Missing)
{
    public static WaterChangeEvidence NoGuidedMode { get; } = new(WaterChangeEvidenceState.None, []);
}

/// <summary>
/// A photo of a guided water change as the evidence judgement sees it: whether the client reported
/// the app camera as its source, when the server received it, its EXIF capture time (null when it
/// has none) with whether that time carries a known UTC offset, and whether the same picture already
/// served as evidence of an earlier water change of the member.
/// </summary>
public sealed record EvidencePhoto(bool CapturedInApp, DateTimeOffset ReceivedAt, DateTimeOffset? TakenAt = null,
    bool OffsetKnown = true, bool ChecksumUsedBefore = false);

/// <summary>
/// Judges the evidence of a finished guided water change from its timer and its photos.
/// </summary>
public static class WaterChangeEvidences
{
    public const string DrainingMissing = "draining_missing";
    public const string DurationTooShort = "duration_too_short";
    public const string PhotoMissing = "photo_missing";
    public const string PhotoOutsideSession = "photo_outside_session";
    public const string PhotoReused = "photo_reused";

    // A capture time without an offset is a wall-clock time of an unknown zone, which can differ
    // from UTC by up to this much.
    private static readonly TimeSpan WidestZoneSpread = TimeSpan.FromHours(14);

    /// <summary>
    /// Verified needs the draining step, at least the minimum from the start of draining to the
    /// refill step (or to the finish when no refill was marked) and at least one qualifying photo.
    /// A photo qualifies when the client reported the app camera, the server received it between the
    /// start of draining and the finish, its EXIF capture time (when present) lies within the session
    /// plus the clock tolerance and the same picture has not served as evidence before. A finished
    /// mode that falls short is Partial and lists every finding in the order draining, duration,
    /// photo. Without any app photo the photo finding is photo_missing; when app photos exist but
    /// none qualifies, it names why they were disqualified (photo_outside_session, photo_reused). A
    /// mode without the draining step has no span to judge the receive time by.
    /// </summary>
    public static WaterChangeEvidence Evaluate(DateTimeOffset sessionStartedAt, DateTimeOffset? drainingStartedAt,
        DateTimeOffset? refillStartedAt, DateTimeOffset finishedAt, TimeSpan minimumDuration, TimeSpan clockTolerance,
        IReadOnlyList<EvidencePhoto> photos)
    {
        List<string> missing = [];
        if (drainingStartedAt is not { } start) missing.Add(DrainingMissing);
        else if ((refillStartedAt ?? finishedAt) - start < minimumDuration) missing.Add(DurationTooShort);

        var inApp = photos.Where(p => p.CapturedInApp).ToList();
        if (inApp.Count == 0) missing.Add(PhotoMissing);
        else
        {
            var outside = inApp.Count(p => IsOutsideSession(p, sessionStartedAt, drainingStartedAt, finishedAt, clockTolerance));
            var reused = inApp.Count(p => !IsOutsideSession(p, sessionStartedAt, drainingStartedAt, finishedAt, clockTolerance) && p.ChecksumUsedBefore);
            if (outside + reused == inApp.Count)
            {
                if (outside > 0) missing.Add(PhotoOutsideSession);
                if (reused > 0) missing.Add(PhotoReused);
            }
        }

        return missing.Count == 0 ? new(WaterChangeEvidenceState.Verified, []) : new(WaterChangeEvidenceState.Partial, missing);
    }

    private static bool IsOutsideSession(EvidencePhoto photo, DateTimeOffset sessionStartedAt, DateTimeOffset? drainingStartedAt,
        DateTimeOffset finishedAt, TimeSpan clockTolerance)
    {
        if (drainingStartedAt is { } draining && (photo.ReceivedAt < draining || photo.ReceivedAt > finishedAt)) return true;
        if (photo.TakenAt is not { } taken) return false;
        var tolerance = photo.OffsetKnown ? clockTolerance : clockTolerance + WidestZoneSpread;
        return taken < sessionStartedAt - tolerance || taken > finishedAt + tolerance;
    }
}
