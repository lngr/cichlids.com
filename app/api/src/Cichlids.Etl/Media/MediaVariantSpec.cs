namespace Cichlids.Etl.Media;

/// <summary>
/// The fixed set of photo renditions the media pipeline derives from every original: four target
/// widths, each with its own label, encoded as JPEG at a fixed quality with orientation baked in
/// and metadata stripped. Videos have no variants; only their original is stored.
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
    /// Resolves which variants to derive from a photo of the given (already orientation-corrected)
    /// width: a target width is only used when the original is wider than it, so no variant ever
    /// upscales. "thumb" is the one exception that always exists exactly once, at the original's
    /// own width when that width does not exceed the thumb target.
    /// </summary>
    public static IReadOnlyList<(string Label, int Width)> Resolve(int originalWidth)
    {
        var result = new List<(string, int)>(Targets.Length);

        foreach (var (label, targetWidth) in Targets)
        {
            if (originalWidth > targetWidth)
            {
                result.Add((label, targetWidth));
            }
            else if (label == "thumb")
            {
                result.Add((label, originalWidth));
            }
        }

        return result;
    }
}
