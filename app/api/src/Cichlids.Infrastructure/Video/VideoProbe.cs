namespace Cichlids.Infrastructure.Video;

/// <summary>
/// What ffprobe reports about a video: its duration, the coded size of the first video stream,
/// the clockwise display rotation in degrees (0, 90, 180 or 270), the codecs of the first video
/// stream and of the first audio stream (null without audio) and the average frame rate (null
/// when unknown). The display size is the coded size with width and height swapped for a quarter
/// turn.
/// </summary>
public sealed record VideoProbe(
    TimeSpan Duration, int Width, int Height, int RotationDegrees, string VideoCodec, string? AudioCodec, double? FrameRate)
{
    public int DisplayWidth => RotationDegrees % 180 == 90 ? Height : Width;

    public int DisplayHeight => RotationDegrees % 180 == 90 ? Width : Height;
}

/// <summary>
/// A video file that cannot be accepted. The code names the reason.
/// </summary>
public sealed class VideoProbeException(string code, string message) : Exception(message)
{
    public const string Unreadable = "video_unreadable";
    public const string NoVideo = "video_no_video_stream";
    public const string UnsupportedCodec = "video_codec_unsupported";
    public const string UnsupportedDimensions = "video_dimensions_unsupported";
    public const string TooLong = "video_too_long";

    public string Code { get; } = code;
}
