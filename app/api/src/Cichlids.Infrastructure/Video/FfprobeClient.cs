namespace Cichlids.Infrastructure.Video;

/// <summary>
/// Reads the properties of a video file with ffprobe. The file must be an MP4 or QuickTime file;
/// ffprobe reads that one local file only, is asked for just the fields the parser needs and its
/// output is bounded in size and time.
/// </summary>
public sealed class FfprobeClient(IProcessRunner runner, VideoToolOptions options)
{
    private const string Entries =
        "stream=codec_type,codec_name,width,height,avg_frame_rate,duration:stream_side_data=rotation:stream_tags=rotate:format=duration";

    /// <summary>
    /// Probes the file at the given path. Throws VideoProbeException when ffprobe cannot read the
    /// file, its output is too large or the file is not an accepted video, and
    /// ProcessTimeoutException when the probe takes longer than the configured probe timeout.
    /// </summary>
    public async Task<VideoProbe> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        FfmpegInput.EnsureMp4Family(path);
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(new ProcessRequest(
                options.FfprobePath,
                ["-v", "error", "-print_format", "json", "-show_entries", Entries, .. FfmpegInput.Arguments(path)],
                options.ProbeTimeout, options.Nice, MaxStandardOutput: FfprobeJsonParser.MaxJsonCharacters), cancellationToken);
        }
        catch (ProcessOutputTooLargeException)
        {
            throw new VideoProbeException(VideoProbeException.Unreadable, "ffprobe printed more than the allowed output.");
        }

        if (result.ExitCode != 0)
        {
            throw new VideoProbeException(VideoProbeException.Unreadable, "ffprobe could not read the file.");
        }

        return FfprobeJsonParser.Parse(result.StandardOutput);
    }
}
