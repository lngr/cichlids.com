namespace Cichlids.Api.Features.Videos;

/// <summary>
/// What one member may upload as a video: the longest length and the largest file size.
/// </summary>
public sealed record VideoLimits(TimeSpan MaxDuration, long MaxBytes)
{
    /// <summary>
    /// The limits for a maximum length: the size limit is that length at the given bitrate in
    /// bits per second, rounded up to whole bytes.
    /// </summary>
    public static VideoLimits For(TimeSpan maxDuration, long maxBitrate) =>
        new(maxDuration, (long)Math.Ceiling(maxDuration.TotalSeconds * maxBitrate / 8));
}
