using System.Buffers.Binary;

namespace Cichlids.Infrastructure.Media.Sanitizing;

/// <summary>
/// Rewrites the chunks of a PNG without touching its image data. Critical chunks, the colour
/// chunks (iCCP, sRGB, gAMA, cHRM, cICP, mDCv, cLLi), transparency, background, significant bits,
/// histogram and suggested palette, physical pixel size and the APNG chunks are kept; the eXIf
/// chunk passes through ExifIfdScrubber with its CRC recomputed. Text chunks (tEXt, zTXt, iTXt),
/// the modification time (tIME) and every other ancillary chunk are dropped, as is everything
/// after IEND. A chunk that runs past the end of the file ends the output there.
/// </summary>
internal static class PngMetadataFilter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static readonly HashSet<string> KeptAncillaryChunks =
    [
        "iCCP", "sRGB", "gAMA", "cHRM", "cICP", "mDCv", "cLLi",
        "tRNS", "bKGD", "sBIT", "hIST", "sPLT", "pHYs",
        "acTL", "fcTL", "fdAT",
    ];

    public static bool Matches(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Signature);

    public static byte[] Filter(byte[] png)
    {
        using var output = new MemoryStream(png.Length);
        output.Write(Signature);
        var position = Signature.Length;

        while (position + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            if (length > png.Length - position - 12)
            {
                break;
            }

            var type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            var chunkEnd = position + 12 + (int)length;
            var isCritical = char.IsAsciiLetterUpper(type[0]);

            if (type == "eXIf")
            {
                var data = png.AsSpan(position + 8, (int)length);
                var scrubbed = data.StartsWith("Exif\0\0"u8) && data.Length > 6
                    ? ExifIfdScrubber.Scrub(data[6..]) is { } inner ? [.. data[..6], .. inner] : null
                    : ExifIfdScrubber.Scrub(data);
                if (scrubbed is not null)
                {
                    WriteChunk(output, png.AsSpan(position + 4, 4), scrubbed);
                }
            }
            else if (isCritical || KeptAncillaryChunks.Contains(type))
            {
                output.Write(png.AsSpan(position, chunkEnd - position));
            }

            position = chunkEnd;
            if (type == "IEND")
            {
                break;
            }
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        output.Write(type);
        output.Write(data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, Crc32(type, data));
        output.Write(checksum);
    }

    // The CRC-32 (ISO 3309) over chunk type and data that every PNG chunk ends with.
    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
