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
/// Judges the evidence of a finished guided water change from its timer and its photos.
/// </summary>
public static class WaterChangeEvidences
{
    public const string DrainingMissing = "draining_missing";
    public const string DurationTooShort = "duration_too_short";
    public const string PhotoMissing = "photo_missing";

    /// <summary>
    /// Counts the photos that were taken with the camera of the app. Each argument is the in-app flag
    /// of one photo slot, null for an empty slot; a picture from the library never counts.
    /// </summary>
    public static int CountInAppPhotos(params bool?[] capturedInApp) => capturedInApp.Count(flag => flag == true);

    /// <summary>
    /// Verified needs the draining step, at least the minimum from the start of draining to the
    /// refill step (or to the finish when no refill was marked) and at least one photo taken with the
    /// camera of the app. A finished mode that falls short is Partial and lists every finding in the
    /// order draining, duration, photo.
    /// </summary>
    public static WaterChangeEvidence Evaluate(DateTimeOffset? drainingStartedAt, DateTimeOffset? refillStartedAt,
        DateTimeOffset finishedAt, TimeSpan minimumDuration, int inAppPhotos)
    {
        List<string> missing = [];
        if (drainingStartedAt is not { } start) missing.Add(DrainingMissing);
        else if ((refillStartedAt ?? finishedAt) - start < minimumDuration) missing.Add(DurationTooShort);
        if (inAppPhotos < 1) missing.Add(PhotoMissing);
        return missing.Count == 0 ? new(WaterChangeEvidenceState.Verified, []) : new(WaterChangeEvidenceState.Partial, missing);
    }
}
