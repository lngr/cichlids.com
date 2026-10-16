namespace Cichlids.Domain;

/// <summary>
/// How well the guided mode proves a water change: Verified when the mode ran with enough time
/// between the start of draining and the end, Partial when it was finished but too short or
/// without the draining step, None for an entry logged without the mode.
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
/// Judges the evidence of a finished guided water change from its timer. Photos are optional
/// attachments and do not take part.
/// </summary>
public static class WaterChangeEvidences
{
    public const string DrainingMissing = "draining_missing";
    public const string DurationTooShort = "duration_too_short";

    /// <summary>
    /// Verified needs the draining step and at least the minimum from the start of draining to the
    /// refill step, or to the finish when no refill was marked. A finished mode that falls short is
    /// Partial.
    /// </summary>
    public static WaterChangeEvidence Evaluate(DateTimeOffset? drainingStartedAt, DateTimeOffset? refillStartedAt,
        DateTimeOffset finishedAt, TimeSpan minimumDuration)
    {
        if (drainingStartedAt is not { } start) return new(WaterChangeEvidenceState.Partial, [DrainingMissing]);
        var end = refillStartedAt ?? finishedAt;
        return end - start < minimumDuration
            ? new(WaterChangeEvidenceState.Partial, [DurationTooShort])
            : new(WaterChangeEvidenceState.Verified, []);
    }
}
