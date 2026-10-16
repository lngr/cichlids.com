using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// The transcoding of one uploaded video, queued until a worker claims it. A worker holds a
/// running job until LockedUntil; a job whose lease has run out is claimed again, so a worker that
/// died does not block the video. Each claim counts as an attempt.
/// </summary>
public class VideoJob
{
    public long Id { get; set; }
    public long MediaItemId { get; set; }
    public VideoJobStatus Status { get; set; }

    /// <summary>
    /// Whether the renditions are written under the private prefix, as for the video of a care
    /// entry, instead of under the public variant prefix.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Number of times a worker has claimed the job.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// The earliest time a queued job is claimed.
    /// </summary>
    public DateTimeOffset RunAfter { get; set; }

    /// <summary>
    /// The end of the lease of a running job; null while the job is not running.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// The problem code of the last failure, or null while no attempt has failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
