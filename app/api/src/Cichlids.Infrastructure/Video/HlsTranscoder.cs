namespace Cichlids.Infrastructure.Video;

/// <summary>
/// The files a transcode wrote below its output directory: the renditions and the paths of all
/// files relative to that directory, using forward slashes.
/// </summary>
public sealed record HlsResult(IReadOnlyList<HlsRendition> Renditions, IReadOnlyList<string> Files);

/// <summary>
/// A failed ffmpeg run. The message holds the tail of ffmpeg's error output.
/// </summary>
public sealed class VideoTranscodeException(string message) : Exception(message);

/// <summary>
/// Transcodes a video into HLS with ffmpeg: one fragmented MP4 rendition per ladder rung, a master
/// playlist and a poster frame, all below one output directory. ffmpeg runs as a child process
/// with a timeout, a lowered priority and a bounded number of threads.
/// </summary>
public sealed class HlsTranscoder(IProcessRunner runner, PosterFrameSelector posterSelector, VideoToolOptions options)
{
    public const string MasterPlaylistName = "master.m3u8";
    public const string PosterName = "poster.jpg";

    /// <summary>
    /// Writes the HLS renditions and the poster of the probed video below the output directory.
    /// Throws VideoTranscodeException when ffmpeg exits with an error or leaves no playlist and
    /// ProcessTimeoutException when it exceeds the transcode timeout.
    /// </summary>
    public async Task<HlsResult> TranscodeAsync(
        string inputPath, VideoProbe probe, string outputDirectory, TimeSpan maxDuration, CancellationToken cancellationToken)
    {
        FfmpegInput.EnsureMp4Family(inputPath);
        if (probe.Duration > maxDuration)
        {
            throw new VideoProbeException(VideoProbeException.TooLong, "The video is longer than the limit.");
        }

        var renditions = HlsRenditionLadder.Resolve(probe);
        Directory.CreateDirectory(outputDirectory);
        foreach (var rendition in renditions)
        {
            Directory.CreateDirectory(Path.Combine(outputDirectory, rendition.Label));
        }

        var arguments = HlsArguments.Build(inputPath, outputDirectory, renditions, probe.AudioCodec is not null, options.Threads,
            probe.FrameRate, maxDuration + TimeSpan.FromSeconds(1));
        var result = await runner.RunAsync(
            new ProcessRequest(options.FfmpegPath, arguments, options.TranscodeTimeout, options.Nice), cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new VideoTranscodeException($"ffmpeg exited with code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        if (!File.Exists(Path.Combine(outputDirectory, MasterPlaylistName)))
        {
            throw new VideoTranscodeException("ffmpeg wrote no master playlist.");
        }

        var decoded = DecodedDuration(Path.Combine(outputDirectory, renditions[0].Label, "index.m3u8"));
        if (decoded > maxDuration + TimeSpan.FromMilliseconds(50))
        {
            throw new VideoProbeException(VideoProbeException.TooLong, "The video is longer than the limit.");
        }

        await posterSelector.ExtractAsync(inputPath, Path.Combine(outputDirectory, PosterName), probe.Duration, outputDirectory, cancellationToken);

        var files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(outputDirectory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        return new HlsResult(renditions, files);
    }

    // The length ffmpeg actually wrote: the sum of the segment durations of a rendition playlist.
    private static TimeSpan DecodedDuration(string playlistPath)
    {
        double seconds = 0;
        foreach (var line in File.ReadLines(playlistPath))
        {
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal)
                && double.TryParse(line["#EXTINF:".Length..].Split(',')[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var segment))
            {
                seconds += segment;
            }
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
