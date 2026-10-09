using NetVips;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Decodes source image bytes and derives JPEG variants from them via libvips (NetVips). Every
/// entry point applies the source's EXIF orientation before reading dimensions or resizing, so a
/// rotated original's "width" is the width a viewer actually sees, and every produced variant
/// carries that same corrected orientation baked into its pixels with the EXIF tag stripped.
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    /// Thrown when source bytes cannot be decoded as an image, either because the format is not
    /// recognized at all or because the data is truncated partway through.
    /// </summary>
    public sealed class BrokenImageException : Exception
    {
        public BrokenImageException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Reads the orientation-corrected dimensions of a source image without producing any output
    /// file.
    /// </summary>
    public static (int Width, int Height) Measure(byte[] sourceBytes)
    {
        using var oriented = LoadOriented(sourceBytes);
        return (oriented.Width, oriented.Height);
    }

    /// <summary>
    /// Produces one JPEG-encoded variant at the target width: never upscales, so a target width
    /// at or above the source's own width leaves the image at its original size. Callers are
    /// expected to have already resolved the target width through MediaVariantSpec.Resolve, which
    /// guarantees that invariant.
    /// </summary>
    public static byte[] GenerateVariant(byte[] sourceBytes, int targetWidth)
    {
        using var oriented = LoadOriented(sourceBytes);

        try
        {
            using var sized = oriented.Width > targetWidth ? oriented.ThumbnailImage(targetWidth) : oriented;
            return sized.WriteToBuffer(".jpg", new VOption { { "Q", MediaVariantSpec.JpegQuality }, { "strip", true } });
        }
        catch (VipsException ex)
        {
            throw new BrokenImageException("Image data could not be resized or encoded.", ex);
        }
    }

    // Loading and auto-rotating is the operation that actually forces libvips to touch pixel data
    // (a bare NewFromFile/NewFromBuffer only reads the header, lazily), so this is where a
    // truncated or otherwise undecodable file surfaces as an exception rather than at a later,
    // unrelated call.
    private static Image LoadOriented(byte[] sourceBytes)
    {
        try
        {
            using var loaded = Image.NewFromBuffer(sourceBytes, access: Enums.Access.Sequential);
            return loaded.Autorot();
        }
        catch (VipsException ex)
        {
            throw new BrokenImageException("Image data could not be decoded.", ex);
        }
    }
}
