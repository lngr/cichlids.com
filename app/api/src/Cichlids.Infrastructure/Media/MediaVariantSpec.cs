namespace Cichlids.Infrastructure.Media;

/// <summary>
/// The fixed set of photo renditions the media pipeline derives from every original: four target
/// widths, each with its own label, encoded as sRGB JPEG at a fixed quality with orientation baked
/// in and metadata stripped. Videos have no variants; only their original is stored.
/// </summary>
public static class MediaVariantSpec
{
    public const int JpegQuality = 82;

    private static readonly (string Label, int TargetWidth)[] Targets =
    [
        ("thumb", 200),
        ("small", 400),
        ("medium", 800),
        ("large", 1600),
    ];

    /// <summary>
    /// The tallest a variant may be, as a multiple of its target width. A photo taller than that
    /// is fitted into its target width times this factor, so very tall images keep bounded
    /// variant sizes and decode memory, far below the JPEG limit of 65535 pixels per side. The
    /// image processor accepts a long side of at most 100 times the short side, so a fitted
    /// variant is at least a few pixels wide.
    /// </summary>
    public const int MaxHeightToWidthRatio = 4;

    /// <summary>
    /// Resolves which variants to derive from a photo of the given (already orientation-corrected)
    /// size and the pixel width of each. A target width is only used when the original is wider
    /// than it, so no variant ever upscales; "thumb" is the one exception that always exists
    /// exactly once, at the original's own width when that width does not exceed the thumb
    /// target. Each variant fits into its target width and MaxHeightToWidthRatio times that width
    /// in height; the returned width is the width the variant has after fitting.
    /// </summary>
    public static IReadOnlyList<(string Label, int Width)> Resolve(int originalWidth, int originalHeight)
    {
        var result = new List<(string, int)>(Targets.Length);

        foreach (var (label, targetWidth) in Targets)
        {
            if (originalWidth > targetWidth)
            {
                result.Add((label, Fit(targetWidth, originalWidth, originalHeight)));
            }
            else if (label == "thumb")
            {
                result.Add((label, Fit(originalWidth, originalWidth, originalHeight)));
            }
        }

        return result;
    }

    private static int Fit(int targetWidth, int originalWidth, int originalHeight)
    {
        long maxHeight = (long)MaxHeightToWidthRatio * targetWidth;
        if ((long)originalHeight * targetWidth <= maxHeight * originalWidth)
        {
            return targetWidth;
        }

        return Math.Max(1, (int)((long)originalWidth * maxHeight / originalHeight));
    }
}
