using System.Buffers.Binary;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// The content type and the declared pixel size of an image file a browser renders.
/// </summary>
public sealed record BrowserImage(string ContentType, int Width, int Height);

/// <summary>
/// Recognises JPEG, PNG, GIF, WebP and BMP files, the formats browsers, Android and iOS render
/// natively, by their signatures and reads the pixel size their headers declare. Only the
/// headers are read, so a file is recognised even when libvips cannot decode its pixel data.
/// </summary>
public static class BrowserImageSniffer
{
    /// <summary>
    /// Returns the content type and declared size of the file, or null when it is in none of the
    /// recognised formats or its header ends before it declares a positive width and height.
    /// </summary>
    public static BrowserImage? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Create("image/jpeg", ReadJpegSize(bytes));
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return bytes.Length >= 24 && bytes[12..16].SequenceEqual("IHDR"u8)
                ? Create("image/png", (ReadInt32BigEndian(bytes[16..]), ReadInt32BigEndian(bytes[20..])))
                : null;
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return bytes.Length >= 10
                ? Create("image/gif", (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..])))
                : null;
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return Create("image/webp", ReadWebpSize(bytes));
        }

        if (bytes.StartsWith("BM"u8))
        {
            return Create("image/bmp", ReadBmpSize(bytes));
        }

        return null;
    }

    private static BrowserImage? Create(string contentType, (long Width, long Height)? size) =>
        size is { Width: > 0 and <= int.MaxValue, Height: > 0 and <= int.MaxValue } declared
            ? new BrowserImage(contentType, (int)declared.Width, (int)declared.Height)
            : null;

    private static long ReadInt32BigEndian(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);

    // The size is in the first start-of-frame segment; every segment before it carries its own
    // length, so the walk skips metadata of any size without reading it.
    private static (long, long)? ReadJpegSize(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return null;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]);
            if (IsStartOfFrame(marker))
            {
                return offset + 9 <= bytes.Length
                    ? (BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]))
                    : null;
            }

            if (length < 2)
            {
                return null;
            }

            offset += 2 + length;
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    private static (long, long)? ReadWebpSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30)
        {
            return null;
        }

        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Frame tag (3 bytes), start code 9D 01 2A, then 14-bit width and height.
            return bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A
                ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF)
                : null;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // Signature 0x2F, then width minus one and height minus one in 14 bits each.
            if (bytes[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Flags (4 bytes), then canvas width minus one and height minus one in 24 bits each.
            return (ReadUInt24LittleEndian(bytes[24..]) + 1, ReadUInt24LittleEndian(bytes[27..]) + 1);
        }

        return null;
    }

    private static long ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    // The DIB header follows the 14-byte file header. Its size tells the core header of OS/2
    // bitmaps (16-bit sides) from the Windows headers (32-bit sides, a negative height for rows
    // stored top down).
    private static (long, long)? ReadBmpSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 18)
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]);
        if (headerSize == 12)
        {
            return bytes.Length >= 22
                ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]))
                : null;
        }

        if (headerSize < 40 || bytes.Length < 26)
        {
            return null;
        }

        return (BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]), Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bytes[22..])));
    }
}
