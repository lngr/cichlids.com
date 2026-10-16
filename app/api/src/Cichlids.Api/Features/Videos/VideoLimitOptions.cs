namespace Cichlids.Api.Features.Videos;

/// <summary>
/// Limits on the videos a member uploads, bound from the "VideoLimits" configuration section.
/// A member who meets the account rule (an account older than MinAccountAge with more than
/// MinPublishedStories published stories) may upload videos up to MaxDuration; every other member
/// is limited to ShortMaxDuration. A provisional profile has the length, upload and video limits of its own.
/// </summary>
public sealed class VideoLimitOptions
{
    public const string SectionName = "VideoLimits";

    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(3);

    public TimeSpan ShortMaxDuration { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan MinAccountAge { get; set; } = TimeSpan.FromDays(365);

    public int MinPublishedStories { get; set; } = 20;

    /// <summary>
    /// The highest bitrate in bits per second a video file is expected to have. The size limit of
    /// an upload is its length limit times this bitrate.
    /// </summary>
    public long MaxBitrate { get; set; } = 25_000_000;

    /// <summary>
    /// Number of unfinished uploads one member may hold at the same time.
    /// </summary>
    public int MaxOpenUploads { get; set; } = 3;

    /// <summary>
    /// The longest video a provisional profile may upload.
    /// </summary>
    public TimeSpan ProvisionalMaxDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of unfinished uploads a provisional profile may hold at the same time.
    /// </summary>
    public int ProvisionalMaxOpenUploads { get; set; } = 1;

    /// <summary>
    /// Number of videos a provisional profile may hold in its media library.
    /// </summary>
    public int ProvisionalMaxVideos { get; set; } = 10;

    /// <summary>
    /// Size of the chunks of an upload in bytes. Every chunk except the last has exactly this
    /// size. At least 5 MiB, the smallest part the object store accepts.
    /// </summary>
    public int ChunkSize { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Time after which an unfinished upload is abandoned and removed.
    /// </summary>
    public TimeSpan UploadLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether the hosted cleanup loop of abandoned uploads runs.
    /// </summary>
    public bool CleanupEnabled { get; set; } = true;

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
}
