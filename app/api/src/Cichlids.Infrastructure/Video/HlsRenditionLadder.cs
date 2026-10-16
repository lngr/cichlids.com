namespace Cichlids.Infrastructure.Video;

/// <summary>
/// One HLS rendition: its label (the short side in pixels plus p), its size and its video bitrate
/// target in kilobits per second.
/// </summary>
public sealed record HlsRendition(string Label, int Width, int Height, int VideoBitrateKbps);

/// <summary>
/// Chooses the HLS renditions of a video. A rung is a short side of 480, 720 or 1080 pixels and
/// is offered only when the source's displayed short side reaches it, so no rendition is larger
/// than the source and none exceeds 1080p. A source below 480 pixels gets one rendition at its
/// own size. Landscape and portrait video are measured by the short side, and every rendition
/// keeps the source's aspect ratio with even width and height.
/// </summary>
public static class HlsRenditionLadder
{
    /// <summary>
    /// The longest side a rendition may have. A rendition of an extreme aspect ratio is scaled
    /// down until its long side fits.
    /// </summary>
    public const int MaxLongSide = 3840;

    private static readonly (int ShortSide, int BitrateKbps)[] Rungs = [(480, 1100), (720, 2500), (1080, 5000)];

    /// <summary>
    /// The renditions for the displayed size of the probed video, from the lowest to the highest
    /// bitrate.
    /// </summary>
    public static IReadOnlyList<HlsRendition> Resolve(VideoProbe probe) => Resolve(probe.DisplayWidth, probe.DisplayHeight);

    /// <summary>
    /// The renditions for a displayed size, from the lowest to the highest bitrate.
    /// </summary>
    public static IReadOnlyList<HlsRendition> Resolve(int displayWidth, int displayHeight)
    {
        var shortSide = Math.Min(displayWidth, displayHeight);
        var renditions = Rungs.Where(r => r.ShortSide <= shortSide)
            .Select(r => Create(r.ShortSide, r.BitrateKbps, displayWidth, displayHeight))
            .ToList();

        if (renditions.Count == 0)
        {
            var even = Math.Max(2, shortSide - (shortSide % 2));
            renditions.Add(Create(even, Math.Max(300, even * 3), displayWidth, displayHeight));
        }

        return renditions;
    }

    private static HlsRendition Create(int shortSide, int bitrateKbps, int sourceWidth, int sourceHeight)
    {
        var landscape = sourceWidth >= sourceHeight;
        var longSide = landscape
            ? Even((double)sourceWidth * shortSide / sourceHeight)
            : Even((double)sourceHeight * shortSide / sourceWidth);
        var (width, height) = landscape ? (longSide, shortSide) : (shortSide, longSide);
        if (shortSide == Math.Min(sourceWidth, sourceHeight) && sourceWidth % 2 == 0 && sourceHeight % 2 == 0)
        {
            (width, height) = (sourceWidth, sourceHeight);
        }

        if (Math.Max(width, height) > MaxLongSide)
        {
            var factor = (double)MaxLongSide / Math.Max(width, height);
            var fitted = Even(Math.Min(width, height) * factor);
            (width, height) = width >= height ? (MaxLongSide, fitted) : (fitted, MaxLongSide);
            shortSide = fitted;
        }

        return new HlsRendition($"{shortSide}p", width, height, bitrateKbps);
    }

    private static int Even(double value)
    {
        return Math.Max(2, 2 * (int)Math.Round(value / 2));
    }
}
