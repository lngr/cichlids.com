using System.Buffers.Binary;

namespace Cichlids.Infrastructure.Media.Sanitizing;

/// <summary>
/// Rewrites the marker segments of a JPEG without touching its encoded image data. It keeps the
/// APP0 JFIF header, the APP2 ICC profile chunks, the APP14 Adobe segment and every table, frame
/// and scan; the first APP1 EXIF segment passes through ExifIfdScrubber and later ones are dropped,
/// since readers use the first. All other APPn segments (XMP, extended XMP, MPF, FlashPix, IPTC and
/// vendor data) and comments are dropped, as is everything after the end-of-image marker of the
/// primary image, where secondary images and gain maps with their own metadata live. A segment that
/// runs past the end of the file ends the output there.
/// </summary>
internal static class JpegMetadataFilter
{
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte Comment = 0xFE;

    public static bool Matches(ReadOnlySpan<byte> bytes) => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    public static byte[] Filter(byte[] jpeg)
    {
        using var output = new MemoryStream();
        var exifSeen = false;
        output.Write(jpeg.AsSpan(0, 2));
        var position = 2;

        while (position < jpeg.Length)
        {
            // Skips bytes outside any segment and the fill bytes in front of a marker.
            if (jpeg[position] != 0xFF)
            {
                position++;
                continue;
            }

            while (position < jpeg.Length && jpeg[position] == 0xFF)
            {
                position++;
            }

            if (position >= jpeg.Length)
            {
                break;
            }

            var marker = jpeg[position++];
            if (marker == EndOfImage)
            {
                output.Write([0xFF, EndOfImage]);
                break;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write([0xFF, marker]);
                continue;
            }

            if (position + 2 > jpeg.Length)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position));
            if (length < 2 || position + length > jpeg.Length)
            {
                break;
            }

            var payload = jpeg.AsSpan(position + 2, length - 2);
            position += length;

            if (marker == StartOfScan)
            {
                WriteSegment(output, marker, payload);
                var scanEnd = FindScanEnd(jpeg, position);
                output.Write(jpeg.AsSpan(position, scanEnd - position));
                position = scanEnd;
                continue;
            }

            if (marker is >= 0xE0 and <= 0xEF or Comment)
            {
                var kept = FilterApplicationSegment(marker, payload, ref exifSeen);
                if (kept is not null)
                {
                    WriteSegment(output, marker, kept);
                }

                continue;
            }

            WriteSegment(output, marker, payload);
        }

        return output.ToArray();
    }

    // Returns the payload to write for an APPn or COM segment, or null to drop it.
    private static byte[]? FilterApplicationSegment(byte marker, ReadOnlySpan<byte> payload, ref bool exifSeen)
    {
        switch (marker)
        {
            case 0xE0 when payload.StartsWith("JFIF\0"u8):
            case 0xE2 when payload.StartsWith("ICC_PROFILE\0"u8):
            case 0xEE when payload.StartsWith("Adobe"u8):
                return payload.ToArray();
            case 0xE1 when payload.StartsWith("Exif\0"u8) && payload.Length > 6 && !exifSeen:
                exifSeen = true;
                var scrubbed = ExifIfdScrubber.Scrub(payload[6..]);
                return scrubbed is null ? null : [.. payload[..6], .. scrubbed];
            default:
                return null;
        }
    }

    // Entropy-coded data ends at the first marker other than a stuffed zero byte or a restart
    // marker; a scan without a following marker runs to the end of the file.
    private static int FindScanEnd(byte[] jpeg, int position)
    {
        while (position + 1 < jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
            {
                position++;
                continue;
            }

            var next = jpeg[position + 1];
            if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            return position;
        }

        return jpeg.Length;
    }

    private static void WriteSegment(Stream output, byte marker, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = [0xFF, marker, 0, 0];
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(payload.Length + 2));
        output.Write(header);
        output.Write(payload);
    }
}
