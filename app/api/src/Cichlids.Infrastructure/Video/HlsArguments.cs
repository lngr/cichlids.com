using System.Globalization;

namespace Cichlids.Infrastructure.Video;

/// <summary>
/// Builds the ffmpeg arguments that turn one video into HLS: a single decode that is split and
/// scaled into one H.264 stream per rendition, AAC audio mapped to every rendition when the
/// video has audio, fragmented MP4 segments of about four seconds and a master playlist. Source
/// metadata and chapters are dropped. Only the first video and the first audio stream are used,
/// the frame rate is at most MaxFrameRate and the output ends after the given duration. The arguments are separate values and never pass through a
/// shell.
/// </summary>
public static class HlsArguments
{
    public const int SegmentSeconds = 4;

    /// <summary>
    /// The highest frame rate of a rendition; faster video is reduced to it.
    /// </summary>
    public const double MaxFrameRate = 30;

    public static IReadOnlyList<string> Build(
        string inputPath, string outputDirectory, IReadOnlyList<HlsRendition> renditions, bool hasAudio, int threads,
        double? frameRate, TimeSpan maxDuration)
    {
        var count = renditions.Count;
        var splitOutputs = string.Concat(Enumerable.Range(0, count).Select(i => $"[s{i}]"));
        var branches = renditions.Select((r, i) =>
            $"[s{i}]scale={r.Width}:{r.Height}:flags=bicubic,format=yuv420p[o{i}]");
        var source = frameRate is null or > MaxFrameRate + 0.001 ? $"[0:v:0]fps={MaxFrameRate:0}," : "[0:v:0]";
        var graph = count == 1
            ? $"{source}scale={renditions[0].Width}:{renditions[0].Height}:flags=bicubic,format=yuv420p[o0]"
            : $"{source}split={count}{splitOutputs};{string.Join(';', branches)}";

        var args = new List<string>
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-threads", threads.ToString(CultureInfo.InvariantCulture),
        };
        args.AddRange(FfmpegInput.Arguments(inputPath));
        args.AddRange(["-t", maxDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), "-dn", "-sn", "-filter_complex", graph]);

        for (var i = 0; i < count; i++)
        {
            args.AddRange(["-map", $"[o{i}]"]);
            if (hasAudio)
            {
                args.AddRange(["-map", "0:a:0"]);
            }
        }

        args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high", "-crf", "23", "-sc_threshold", "0"]);
        args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{SegmentSeconds})"]);
        for (var i = 0; i < count; i++)
        {
            var bitrate = renditions[i].VideoBitrateKbps;
            args.AddRange([$"-maxrate:v:{i}", $"{bitrate}k", $"-bufsize:v:{i}", $"{bitrate * 2}k"]);
        }

        if (hasAudio)
        {
            args.AddRange(["-c:a", "aac", "-b:a", "128k", "-ac", "2"]);
        }

        args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]);

        var streamMap = string.Join(' ', renditions.Select((r, i) => hasAudio ? $"v:{i},a:{i},name:{r.Label}" : $"v:{i},name:{r.Label}"));
        args.AddRange(
        [
            "-f", "hls",
            "-hls_time", SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_playlist_type", "vod",
            "-hls_segment_type", "fmp4",
            "-hls_flags", "independent_segments",
            "-hls_fmp4_init_filename", "init.mp4",
            "-hls_segment_filename", $"{outputDirectory}/%v/seg_%05d.m4s",
            "-master_pl_name", "master.m3u8",
            "-var_stream_map", streamMap,
            $"{outputDirectory}/%v/index.m3u8",
        ]);
        return args;
    }
}
