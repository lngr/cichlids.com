namespace Cichlids.Infrastructure.Video;

/// <summary>
/// Settings of the external video tools, bound from the "VideoTools" configuration section: where
/// ffmpeg and ffprobe are, how long a probe and a transcode may take, how many threads ffmpeg
/// uses and how nice it runs.
/// </summary>
public sealed class VideoToolOptions
{
    public const string SectionName = "VideoTools";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>
    /// Time a probe or the extraction of one frame may take.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Time the transcoding of one video may take before ffmpeg is stopped.
    /// </summary>
    public TimeSpan TranscodeTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Number of threads ffmpeg uses for one transcode.
    /// </summary>
    public int Threads { get; set; } = 2;

    /// <summary>
    /// Niceness ffmpeg runs with, 19 being the lowest priority.
    /// </summary>
    public int Nice { get; set; } = 15;
}
