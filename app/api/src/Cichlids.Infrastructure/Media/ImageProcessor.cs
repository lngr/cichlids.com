using NetVips;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Turns source image bytes into the JPEG variants the media pipeline serves, via libvips
/// (NetVips). Only JPEG, PNG, WebP, GIF and TIFF are accepted, and an image whose header declares
/// more than MaxPixels pixels is rejected before any pixel is decoded. The source is decoded once,
/// directly at the largest variant width, with its EXIF orientation applied and its colours
/// converted to sRGB (an embedded ICC profile is honoured, untagged CMYK uses the built-in CMYK
/// profile); the smaller variants are derived from that decoded image. Every variant is an
/// untagged sRGB JPEG without EXIF, XMP, IPTC or ICC data.
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    /// Largest accepted image in pixels (width times height), 100 megapixels.
    /// </summary>
    public const long MaxPixels = 100_000_000;

    // libvips' largest image dimension; as the thumbnail height it leaves the width as the only
    // constraint.
    private const int UnconstrainedHeight = 10_000_000;

    // libvips class names of the loaders the allowlist admits; every subclass (file, buffer and
    // source variants) is admitted with its class.
    private static readonly (string Loader, ImageFormat Format)[] AllowedLoaders =
    [
        ("VipsForeignLoadJpeg", ImageFormat.Jpeg),
        ("VipsForeignLoadPng", ImageFormat.Png),
        ("VipsForeignLoadWebp", ImageFormat.Webp),
        ("VipsForeignLoadNsgif", ImageFormat.Gif),
        ("VipsForeignLoadTiff", ImageFormat.Tiff),
    ];

    // Blocking applies to the whole process, so no code path can reach an SVG, PDF, HEIF or other
    // loader through libvips. The operation cache is off because it would keep decoded images of
    // untrusted input alive between unrelated calls.
    static ImageProcessor()
    {
        Cache.Max = 0;
        Operation.Block("VipsForeignLoad", true);
        foreach (var (loader, _) in AllowedLoaders)
        {
            Operation.Block(loader, false);
        }
    }

    /// <summary>
    /// Thrown when source bytes cannot be decoded as an image, for example because the data is
    /// truncated or corrupt.
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
    /// Processes an image held in memory. See the stream overload for the result and the
    /// exceptions.
    /// </summary>
    public static ProcessedImage Process(byte[] sourceBytes)
    {
        using var source = Source.NewFromMemory(sourceBytes);
        return Process(source);
    }

    /// <summary>
    /// Reads the format and the orientation-corrected size of an image and produces every variant
    /// MediaVariantSpec resolves for that width, reading the stream once. Throws
    /// UnsupportedImageFormatException for a format outside the allowlist, ImageTooLargeException
    /// for more than MaxPixels pixels and BrokenImageException when the data does not decode.
    /// </summary>
    public static ProcessedImage Process(Stream source)
    {
        using var custom = new SourceCustom();
        custom.OnRead += (buffer, length) => source.Read(buffer, 0, length);
        custom.OnSeek += (offset, origin) => source.CanSeek ? source.Seek(offset, origin) : -1;
        return Process(custom);
    }

    private static ProcessedImage Process(Source source)
    {
        var format = DetectFormat(source);

        try
        {
            var (width, height) = ReadOrientedSize(source);
            var targets = MediaVariantSpec.Resolve(width);
            var largestWidth = targets.Max(t => t.Width);

            using var decoded = DecodeToSrgb(source, largestWidth);
            var variants = new List<ImageVariant>(targets.Count);
            foreach (var (label, targetWidth) in targets)
            {
                if (targetWidth >= decoded.Width)
                {
                    variants.Add(new ImageVariant(label, targetWidth, Encode(decoded)));
                    continue;
                }

                using var resized = decoded.ThumbnailImage(targetWidth, height: UnconstrainedHeight, size: Enums.Size.Down);
                variants.Add(new ImageVariant(label, targetWidth, Encode(resized)));
            }

            return new ProcessedImage(format, width, height, variants);
        }
        catch (VipsException ex)
        {
            throw new BrokenImageException("Image data could not be decoded.", ex);
        }
    }

    private static ImageFormat DetectFormat(Source source)
    {
        var loader = Image.FindLoadSource(source);
        foreach (var (allowed, format) in AllowedLoaders)
        {
            if (loader is not null && loader.StartsWith(allowed, StringComparison.Ordinal))
            {
                return format;
            }
        }

        throw new UnsupportedImageFormatException();
    }

    // Reads the header only; the declared size is checked here, before any pixel is decoded.
    private static (int Width, int Height) ReadOrientedSize(Source source)
    {
        using var header = Image.NewFromSource(source, access: Enums.Access.Sequential);
        if ((long)header.Width * header.Height > MaxPixels)
        {
            throw new ImageTooLargeException(header.Width, header.Height);
        }

        var orientation = header.GetTypeOf("orientation") != 0 ? (int)header.Get("orientation") : 1;
        return orientation is >= 5 and <= 8 ? (header.Height, header.Width) : (header.Width, header.Height);
    }

    // The thumbnail operation shrinks during decode where the format allows it, applies the EXIF
    // orientation and converts to sRGB. Copying to memory decodes the whole source once, so the
    // smaller variants resize from memory.
    private static Image DecodeToSrgb(Source source, int width)
    {
        using var thumbnail = (Image)Operation.Call(
            "thumbnail_source",
            new VOption
            {
                { "height", UnconstrainedHeight },
                { "size", Enums.Size.Down },
                { "export_profile", "srgb" },
            },
            source,
            width);
        return thumbnail.CopyMemory();
    }

    private static byte[] Encode(Image image) =>
        image.WriteToBuffer(".jpg", new VOption { { "Q", MediaVariantSpec.JpegQuality }, { "keep", Enums.ForeignKeep.None } });
}

/// <summary>
/// An image format the image processor accepts.
/// </summary>
public enum ImageFormat
{
    Jpeg,
    Png,
    Webp,
    Gif,
    Tiff,
}

/// <summary>
/// One JPEG variant: its label, its target width in pixels and its encoded bytes.
/// </summary>
public sealed record ImageVariant(string Label, int Width, byte[] Bytes);

/// <summary>
/// The format and the orientation-corrected width and height of a processed image, and its
/// variants from the smallest to the largest.
/// </summary>
public sealed record ProcessedImage(ImageFormat Format, int Width, int Height, IReadOnlyList<ImageVariant> Variants);
