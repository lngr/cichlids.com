using NetVips;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Decodes source image bytes and derives JPEG variants from them via libvips (NetVips). Only
/// JPEG, PNG, WebP, GIF and TIFF are accepted, and an image whose header declares more than
/// MaxPixels pixels is rejected before any pixel is decoded. Every entry point applies the
/// source's EXIF orientation before reading dimensions or resizing, so a rotated original's
/// "width" is the width a viewer actually sees, and every produced variant has that same
/// corrected orientation baked into its pixels with the EXIF tag stripped.
/// </summary>
public static class ImageProcessor
{
    // libvips class names of the loaders the allowlist admits; every subclass (file, buffer and
    // source variants) is admitted with its class.
    private static readonly string[] AllowedLoaders =
    [
        "VipsForeignLoadJpeg",
        "VipsForeignLoadPng",
        "VipsForeignLoadWebp",
        "VipsForeignLoadNsgif",
        "VipsForeignLoadTiff",
    ];

    // Blocking applies to the whole process, so no code path can reach an SVG, PDF, HEIF or other
    // loader through libvips. The operation cache is off because it would keep decoded images of
    // untrusted input alive between unrelated calls.
    static ImageProcessor()
    {
        Cache.Max = 0;
        Operation.Block("VipsForeignLoad", true);
        foreach (var loader in AllowedLoaders)
        {
            Operation.Block(loader, false);
        }
    }

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
    /// Thrown when an image declares more pixels than MaxPixels allows.
    /// </summary>
    public sealed class ImageTooLargeException(int width, int height)
        : Exception($"The image has {width}x{height} pixels, more than {MaxPixels / 1_000_000} megapixels.");

    /// <summary>
    /// Thrown when source bytes are in a format outside the allowlist of JPEG, PNG, WebP, GIF and
    /// TIFF, or in no format libvips recognises.
    /// </summary>
    public sealed class UnsupportedImageFormatException()
        : Exception("The image format is not supported; accepted formats are JPEG, PNG, WebP, GIF and TIFF.");

    /// <summary>
    /// Largest accepted image in pixels (width times height), 100 megapixels.
    /// </summary>
    public const long MaxPixels = 100_000_000;

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

    // Checks the format and the declared size before any pixel is decoded, then loads and
    // auto-rotates. Auto-rotating is the operation that forces libvips to touch pixel data (a bare
    // NewFromBuffer only reads the header, lazily), so this is where a truncated or otherwise
    // undecodable file surfaces as an exception.
    private static Image LoadOriented(byte[] sourceBytes)
    {
        var loader = Image.FindLoadBuffer(sourceBytes);
        if (loader is null || !AllowedLoaders.Any(allowed => loader.StartsWith(allowed, StringComparison.Ordinal)))
        {
            throw new UnsupportedImageFormatException();
        }

        try
        {
            using var loaded = Image.NewFromBuffer(sourceBytes, access: Enums.Access.Sequential);
            if ((long)loaded.Width * loaded.Height > MaxPixels)
            {
                throw new ImageTooLargeException(loaded.Width, loaded.Height);
            }

            return loaded.Autorot();
        }
        catch (VipsException ex)
        {
            throw new BrokenImageException("Image data could not be decoded.", ex);
        }
    }
}
