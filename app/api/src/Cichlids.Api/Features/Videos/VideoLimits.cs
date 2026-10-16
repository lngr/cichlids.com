namespace Cichlids.Api.Features.Videos;

/// <summary>
/// What one member may upload as a video: the longest length, the largest file size, the number of
/// unfinished uploads at the same time and, where the account limits it, the number of videos the
/// media library holds.
/// </summary>
public sealed record VideoLimits(TimeSpan MaxDuration, long MaxBytes, int MaxOpenUploads = int.MaxValue, int? MaxVideos = null)
{
    /// <summary>
    /// The limits for a maximum length: the size limit is that length at the given bitrate in
    /// bits per second, rounded up to whole bytes.
    /// </summary>
    public static VideoLimits For(TimeSpan maxDuration, long maxBitrate, int maxOpenUploads = int.MaxValue, int? maxVideos = null) =>
        new(maxDuration, (long)Math.Ceiling(maxDuration.TotalSeconds * maxBitrate / 8), maxOpenUploads, maxVideos);
}
