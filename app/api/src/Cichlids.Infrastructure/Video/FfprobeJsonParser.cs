using System.Globalization;
using System.Text.Json;

namespace Cichlids.Infrastructure.Video;

/// <summary>
/// Reads the JSON ffprobe prints into a VideoProbe. The JSON comes from an untrusted file, so
/// its size, nesting, stream count and every value are bounded and validated: the first video
/// and the first audio stream are read, the video codec must be H.264 or HEVC, the audio codec
/// one of a known set, the duration finite, positive and at most a day, the size between
/// MinSide and MaxSide with an aspect ratio of at most MaxAspectRatio, the frame rate at most
/// MaxFrameRate and the rotation a quarter turn. Anything else refuses the file with a coded
/// VideoProbeException.
/// </summary>
public static class FfprobeJsonParser
{
    public const int MaxJsonCharacters = 64 * 1024;
    public const int MaxStreams = 32;
    public const int MaxSideData = 16;
    public const int MaxDepth = 8;
    public const int MinSide = 16;
    public const int MaxSide = 8192;
    public const int MaxAspectRatio = 4;
    public const double MaxFrameRate = 240;
    public const double MaxDurationSeconds = 24 * 60 * 60;
    private const int MaxCodecNameLength = 32;

    private static readonly string[] VideoCodecs = ["h264", "hevc"];

    private static readonly string[] AudioCodecs =
        ["aac", "mp3", "ac3", "eac3", "opus", "vorbis", "flac", "alac", "pcm_s16le", "pcm_s16be", "pcm_s24le", "pcm_f32le", "amr_nb", "amr_wb"];

    public static VideoProbe Parse(string json)
    {
        if (json.Length > MaxJsonCharacters)
        {
            throw Refuse("ffprobe printed more than the allowed output.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            throw Refuse("ffprobe did not print JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("streams", out var streams)
                || streams.ValueKind != JsonValueKind.Array)
            {
                throw Refuse("ffprobe found no streams.");
            }

            var count = streams.GetArrayLength();
            if (count == 0 || count > MaxStreams)
            {
                throw Refuse($"The file has {count} streams.");
            }

            var video = streams.EnumerateArray().FirstOrDefault(s => CodecType(s) == "video");
            if (video.ValueKind != JsonValueKind.Object)
            {
                throw new VideoProbeException(VideoProbeException.NoVideo, "The file has no video stream.");
            }

            var audio = streams.EnumerateArray().FirstOrDefault(s => CodecType(s) == "audio");
            var codec = Text(video, "codec_name") ?? string.Empty;
            if (!VideoCodecs.Contains(codec))
            {
                throw new VideoProbeException(VideoProbeException.UnsupportedCodec, "The video codec is not supported.");
            }

            string? audioCodec = null;
            if (audio.ValueKind == JsonValueKind.Object)
            {
                audioCodec = Text(audio, "codec_name") ?? string.Empty;
                if (!AudioCodecs.Contains(audioCodec))
                {
                    throw new VideoProbeException(VideoProbeException.UnsupportedCodec, "The audio codec is not supported.");
                }
            }

            var duration = ReadDuration(root, video);
            var width = Int(video, "width");
            var height = Int(video, "height");
            if (width <= 0 || height <= 0)
            {
                throw Refuse("The video stream has no size.");
            }

            if (Math.Min(width, height) < MinSide || Math.Max(width, height) > MaxSide
                || Math.Max(width, height) > (long)MaxAspectRatio * Math.Min(width, height))
            {
                throw new VideoProbeException(
                    VideoProbeException.UnsupportedDimensions, $"The video size {width}x{height} is not supported.");
            }

            return new VideoProbe(duration, width, height, ReadRotation(video), codec, audioCodec, ReadFrameRate(video));
        }
    }

    private static VideoProbeException Refuse(string message) => new(VideoProbeException.Unreadable, message);

    private static string? CodecType(JsonElement stream) => Text(stream, "codec_type");

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return text is { Length: <= MaxCodecNameLength * 4 } ? text : null;
    }

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static TimeSpan ReadDuration(JsonElement root, JsonElement video)
    {
        var text = root.TryGetProperty("format", out var format) ? Text(format, "duration") : null;
        text ??= Text(video, "duration");
        if (text is null
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds <= 0 || seconds > MaxDurationSeconds)
        {
            throw Refuse("The file has no readable duration.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static int ReadRotation(JsonElement video)
    {
        double? degrees = null;
        if (video.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
        {
            if (sideData.GetArrayLength() > MaxSideData)
            {
                throw Refuse("The video stream has too many side data entries.");
            }

            foreach (var entry in sideData.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("rotation", out var rotation)
                    && rotation.ValueKind == JsonValueKind.Number)
                {
                    degrees = rotation.GetDouble();
                }
            }
        }

        if (degrees is null && video.TryGetProperty("tags", out var tags) && Text(tags, "rotate") is { } tag
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var tagged))
        {
            degrees = tagged;
        }

        if (degrees is not { } value)
        {
            return 0;
        }

        if (!double.IsFinite(value) || Math.Abs(value) > 3600)
        {
            throw new VideoProbeException(VideoProbeException.UnsupportedDimensions, "The rotation is not supported.");
        }

        var quarter = (int)Math.Round(value / 90);
        if (Math.Abs(value - (quarter * 90)) > 1)
        {
            throw new VideoProbeException(VideoProbeException.UnsupportedDimensions, "The rotation is not a quarter turn.");
        }

        return (((quarter % 4) + 4) % 4) * 90;
    }

    private static double? ReadFrameRate(JsonElement video)
    {
        var text = Text(video, "avg_frame_rate");
        var parts = text?.Split('/');
        if (parts is not { Length: 2 }
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            || !double.IsFinite(numerator) || !double.IsFinite(denominator))
        {
            return null;
        }

        if (numerator < 0 || denominator < 0)
        {
            throw Refuse("The frame rate is negative.");
        }

        if (numerator == 0 || denominator == 0)
        {
            return null;
        }

        var rate = numerator / denominator;
        if (rate > MaxFrameRate)
        {
            throw Refuse("The frame rate is too high.");
        }

        return rate;
    }
}
