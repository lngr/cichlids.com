using Cichlids.Infrastructure.Media.Sanitizing;
using NetVips;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Removes location data, serial numbers, owner names and other personal metadata from an image
/// file while keeping its encoded image data byte for byte. Capture date, orientation, colour
/// profile, camera make and model, artist and copyright stay. JPEG, PNG, WebP and GIF are
/// filtered at the container level in memory-safe code. TIFF is re-encoded by libvips with
/// identical pixels and only its ICC profile; a TIFF libvips cannot decode is refused. BMP has no
/// metadata container and comes back unchanged, as do bytes in any other format.
/// </summary>
public static class MediaMetadataSanitizer
{
    /// <summary>
    /// Thrown for an image whose metadata cannot be removed because its pixels do not decode, so
    /// it cannot be stored without its personal metadata.
    /// </summary>
    public sealed class UnsanitizableImageException(string message, Exception inner) : Exception(message, inner);

    /// <summary>
    /// Returns the sanitized bytes of an image file. Throws ImageProcessor.ImageTooLargeException
    /// for a TIFF whose declared size ImageProcessor.CheckDeclaredSize rejects and
    /// UnsanitizableImageException for a TIFF that does not decode.
    /// </summary>
    public static byte[] Sanitize(byte[] source)
    {
        if (JpegMetadataFilter.Matches(source))
        {
            return JpegMetadataFilter.Filter(source);
        }

        if (PngMetadataFilter.Matches(source))
        {
            return PngMetadataFilter.Filter(source);
        }

        if (WebpMetadataFilter.Matches(source))
        {
            return WebpMetadataFilter.Filter(source);
        }

        if (GifMetadataFilter.Matches(source))
        {
            return GifMetadataFilter.Filter(source);
        }

        if (IsTiff(source))
        {
            return ReencodeTiff(source);
        }

        return source;
    }

    private static bool IsTiff(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("II\x2A\0"u8) || bytes.StartsWith("MM\0\x2A"u8);

    // Deflate compression is lossless, so the pixels stay identical; only the ICC profile is
    // written next to them.
    private static byte[] ReencodeTiff(byte[] source)
    {
        ImageProcessor.EnsureLibvipsConfigured();
        try
        {
            using var image = Image.NewFromBuffer(source);
            ImageProcessor.CheckDeclaredSize(image.Width, image.Height);

            return image.WriteToBuffer(
                ".tif",
                new VOption { { "compression", Enums.ForeignTiffCompression.Deflate }, { "keep", Enums.ForeignKeep.Icc } });
        }
        catch (VipsException ex)
        {
            throw new UnsanitizableImageException("The TIFF image does not decode, so its metadata cannot be removed.", ex);
        }
    }
}
