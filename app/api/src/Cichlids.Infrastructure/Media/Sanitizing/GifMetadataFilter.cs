namespace Cichlids.Infrastructure.Media.Sanitizing;

/// <summary>
/// Rewrites the blocks of a GIF without touching its image data. Image descriptors with their
/// data, graphic control and plain text extensions and the NETSCAPE2.0, ANIMEXTS1.0 and
/// ICCRGBG1 application extensions are kept; comment extensions, XMP and every other application
/// extension are dropped, as is everything after the trailer. A block that runs past the end of
/// the file ends the output there.
/// </summary>
internal static class GifMetadataFilter
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte Trailer = 0x3B;
    private const byte GraphicControlLabel = 0xF9;
    private const byte PlainTextLabel = 0x01;
    private const byte ApplicationLabel = 0xFF;

    public static bool Matches(ReadOnlySpan<byte> bytes) => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8);

    public static byte[] Filter(byte[] gif)
    {
        if (gif.Length < 13)
        {
            return gif;
        }

        var position = 13;
        if ((gif[10] & 0x80) != 0)
        {
            position += 3 * (1 << ((gif[10] & 0x07) + 1));
        }

        using var output = new MemoryStream(gif.Length);
        output.Write(gif.AsSpan(0, Math.Min(position, gif.Length)));

        while (position < gif.Length)
        {
            var start = position;
            var introducer = gif[position];
            if (introducer == Trailer)
            {
                output.WriteByte(Trailer);
                break;
            }

            int? end;
            bool keep;
            if (introducer == ImageSeparator)
            {
                end = SkipImage(gif, position);
                keep = true;
            }
            else if (introducer == ExtensionIntroducer && position + 1 < gif.Length)
            {
                var label = gif[position + 1];
                end = SkipSubBlocks(gif, position + 2);
                keep = label is GraphicControlLabel or PlainTextLabel
                    || (label == ApplicationLabel && IsKeptApplication(gif, position + 2));
            }
            else
            {
                break;
            }

            if (end is null)
            {
                break;
            }

            if (keep)
            {
                output.Write(gif.AsSpan(start, end.Value - start));
            }

            position = end.Value;
        }

        return output.ToArray();
    }

    private static bool IsKeptApplication(byte[] gif, int position)
    {
        if (position + 12 > gif.Length || gif[position] != 11)
        {
            return false;
        }

        var identifier = gif.AsSpan(position + 1, 11);
        return identifier.SequenceEqual("NETSCAPE2.0"u8)
            || identifier.SequenceEqual("ANIMEXTS1.0"u8)
            || identifier.StartsWith("ICCRGBG1"u8);
    }

    // An image descriptor is 10 bytes, optionally followed by a local colour table, then the LZW
    // minimum code size and the data sub-blocks.
    private static int? SkipImage(byte[] gif, int position)
    {
        if (position + 10 > gif.Length)
        {
            return null;
        }

        var packed = gif[position + 9];
        position += 10;
        if ((packed & 0x80) != 0)
        {
            position += 3 * (1 << ((packed & 0x07) + 1));
        }

        return SkipSubBlocks(gif, position + 1);
    }

    private static int? SkipSubBlocks(byte[] gif, int position)
    {
        while (position < gif.Length)
        {
            var length = gif[position];
            position += 1 + length;
            if (length == 0)
            {
                return position;
            }
        }

        return null;
    }
}
