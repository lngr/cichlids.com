using System.Buffers.Binary;
using System.Text;

namespace Cichlids.Infrastructure.Media.Sanitizing;

/// <summary>
/// Rewrites the chunks of an extended WebP (VP8X) without touching its image data. The image,
/// alpha, animation and ICCP chunks are kept and the EXIF chunk passes through ExifIfdScrubber;
/// the XMP chunk and unknown chunks are dropped. The VP8X flags and the RIFF size are updated to
/// match. A simple WebP holds image data only and is cut to its RIFF size.
/// </summary>
internal static class WebpMetadataFilter
{
    private const byte IccFlag = 0x20;
    private const byte ExifFlag = 0x08;
    private const byte XmpFlag = 0x04;

    private static readonly HashSet<string> KeptChunks = ["VP8X", "VP8 ", "VP8L", "ALPH", "ANIM", "ANMF", "ICCP"];

    public static bool Matches(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);

    public static byte[] Filter(byte[] webp)
    {
        var riffEnd = (int)Math.Min(webp.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(4)));
        using var body = new MemoryStream(webp.Length);
        body.Write("WEBP"u8);

        var position = 12;
        var flagsPosition = -1;
        var hasExif = false;
        var hasIcc = false;
        while (position + 8 <= riffEnd)
        {
            var fourCc = Encoding.ASCII.GetString(webp, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(position + 4));
            if (size > riffEnd - position - 8)
            {
                break;
            }

            var data = webp.AsSpan(position + 8, (int)size);
            position += 8 + (int)size + (int)(size & 1);

            if (fourCc == "EXIF")
            {
                var scrubbed = data.StartsWith("Exif\0\0"u8) && data.Length > 6
                    ? ExifIfdScrubber.Scrub(data[6..]) is { } inner ? [.. data[..6], .. inner] : null
                    : ExifIfdScrubber.Scrub(data);
                if (scrubbed is not null)
                {
                    WriteChunk(body, fourCc, scrubbed);
                    hasExif = true;
                }
            }
            else if (KeptChunks.Contains(fourCc))
            {
                if (fourCc == "VP8X")
                {
                    flagsPosition = (int)body.Position + 8;
                }

                hasIcc |= fourCc == "ICCP";
                WriteChunk(body, fourCc, data);
            }
        }

        var bodyBytes = body.ToArray();
        if (flagsPosition >= 0 && flagsPosition < bodyBytes.Length)
        {
            var flags = (byte)(bodyBytes[flagsPosition] & ~(IccFlag | ExifFlag | XmpFlag));
            flags |= (byte)((hasIcc ? IccFlag : 0) | (hasExif ? ExifFlag : 0));
            bodyBytes[flagsPosition] = flags;
        }

        var output = new byte[8 + bodyBytes.Length];
        "RIFF"u8.CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)bodyBytes.Length);
        bodyBytes.CopyTo(output, 8);
        return output;
    }

    private static void WriteChunk(Stream output, string fourCc, ReadOnlySpan<byte> data)
    {
        output.Write(Encoding.ASCII.GetBytes(fourCc));
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        output.Write(size);
        output.Write(data);
        if ((data.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }
}
