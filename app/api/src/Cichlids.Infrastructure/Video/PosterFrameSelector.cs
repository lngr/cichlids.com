using NetVips;

namespace Cichlids.Infrastructure.Video;

/// <summary>
/// Chooses the poster frame of a video. A few frames spread over the video are extracted as
/// candidates and the one with the highest sharpness wins, so a motion-blurred or flat frame is
/// not picked. Sharpness is the variance of the Laplacian of the grayscale frame.
/// </summary>
public sealed class PosterFrameSelector(IProcessRunner runner, VideoToolOptions options)
{
    private const int ScoringWidth = 320;
    private static readonly double[,] Laplacian = { { 0, 1, 0 }, { 1, -4, 1 }, { 0, 1, 0 } };

    /// <summary>
    /// The moments of the candidate frames: one in the middle of a video shorter than a second,
    /// three for a video shorter than three seconds and five spread over a longer one.
    /// </summary>
    public static IReadOnlyList<TimeSpan> CandidateTimes(TimeSpan duration)
    {
        double[] fractions = duration.TotalSeconds < 1 ? [0.5]
            : duration.TotalSeconds < 3 ? [0.25, 0.5, 0.75]
            : [0.1, 0.3, 0.5, 0.7, 0.9];
        return fractions.Select(f => duration * f).ToList();
    }

    /// <summary>
    /// The variance of the Laplacian of the image at the given path, scaled to a fixed width. A
    /// higher value means more fine detail.
    /// </summary>
    public static double Sharpness(string imagePath)
    {
        using var thumbnail = Image.Thumbnail(imagePath, ScoringWidth);
        using var gray = thumbnail.Colourspace(Enums.Interpretation.Bw);
        using var band = gray.Bands > 1 ? gray.ExtractBand(0) : gray.Copy();
        using var kernel = Image.NewFromArray(Laplacian);
        using var filtered = band.Conv(kernel);
        var deviation = filtered.Deviate();
        return deviation * deviation;
    }

    /// <summary>
    /// Extracts the candidate frames of the video at the input path, writes the sharpest to the
    /// output path as a JPEG no wider than 1280 pixels and removes the other candidates. The frames
    /// are taken upright, with the rotation of the video applied.
    /// </summary>
    public async Task ExtractAsync(string inputPath, string outputPath, TimeSpan duration, string workingDirectory, CancellationToken cancellationToken)
    {
        FfmpegInput.EnsureMp4Family(inputPath);
        var candidates = new List<string>();
        var index = 0;
        foreach (var time in CandidateTimes(duration))
        {
            var path = Path.Combine(workingDirectory, $"candidate-{index++}.jpg");
            var result = await runner.RunAsync(new ProcessRequest(
                options.FfmpegPath,
                [
                    "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-ss",
                    time.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    .. FfmpegInput.Arguments(inputPath), "-frames:v", "1", "-dn", "-sn", "-vf", "scale='min(1280,iw)':-2", "-q:v", "3", "-map_metadata", "-1", path,
                ],
                options.ProbeTimeout, options.Nice), cancellationToken);
            if (result.ExitCode == 0 && File.Exists(path))
            {
                candidates.Add(path);
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No poster frame could be extracted.");
        }

        var best = candidates.MaxBy(Sharpness)!;
        File.Move(best, outputPath, overwrite: true);
        foreach (var candidate in candidates.Where(c => c != best))
        {
            File.Delete(candidate);
        }
    }
}
