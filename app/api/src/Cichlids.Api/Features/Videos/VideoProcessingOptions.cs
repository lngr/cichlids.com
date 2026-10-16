namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Settings of the video transcoding queue, bound from the "VideoTranscoding" configuration
/// section.
/// </summary>
public sealed class VideoProcessingOptions
{
    public const string SectionName = "VideoTranscoding";

    /// <summary>
    /// Whether the hosted worker that processes the queue runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time the worker waits when the queue is empty.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Number of times a job is attempted before it is marked failed.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Delay before a failed job is attempted again, multiplied by the attempts so far.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Time a worker holds a claimed job. A job whose worker does not finish within the lease is
    /// claimed by another worker.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(35);
}

/// <summary>
/// Problem codes of a video whose processing failed.
/// </summary>
public static class VideoProcessingErrors
{
    public const string ProcessingFailed = "video_processing_failed";
}
