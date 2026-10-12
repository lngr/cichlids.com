using System.Buffers.Binary;
using System.Text;

namespace Cichlids.Tests.Shared;

/// <summary>
/// One TIFF directory entry for ExifBuilder: tag, field type, value count and the value bytes
/// in the byte order of the block being built.
/// </summary>
internal sealed record ExifEntry(ushort Tag, ushort Type, uint Count, byte[] Value);

/// <summary>
/// Builds TIFF-structured EXIF blocks and whole baseline TIFF images byte by byte, so tests
/// control every directory, pointer and value: IFD0, the Exif IFD, the Interop IFD, the GPS IFD
/// and IFD1 with an embedded thumbnail. Values longer than four bytes are stored after their
/// directory; pointer entries are added for every non-empty sub-directory.
/// </summary>
internal sealed class ExifBuilder
{
    public const ushort ExifPointerTag = 0x8769;
    public const ushort GpsPointerTag = 0x8825;
    public const ushort InteropPointerTag = 0xA005;

    public ExifBuilder(bool bigEndian = false)
    {
        BigEndian = bigEndian;
    }

    public bool BigEndian { get; }

    public List<ExifEntry> Ifd0 { get; } = [];

    public List<ExifEntry> Exif { get; } = [];

    public List<ExifEntry> Interop { get; } = [];

    public List<ExifEntry> Gps { get; } = [];

    /// <summary>
    /// JPEG bytes stored as the IFD1 thumbnail, or null for no IFD1.
    /// </summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// When true the IFD1 thumbnail offset and length are written as SHORT values.
    /// </summary>
    public bool ThumbnailPointersAsShort { get; set; }

    /// <summary>
    /// When true the Exif IFD pointer of IFD0 points back at IFD0 itself.
    /// </summary>
    public bool LoopExifPointerToIfd0 { get; set; }

    public ExifEntry Ascii(ushort tag, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        return new ExifEntry(tag, 2, (uint)bytes.Length, bytes);
    }

    public ExifEntry Short(ushort tag, params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            WriteU16(bytes.AsSpan(i * 2), values[i]);
        }

        return new ExifEntry(tag, 3, (uint)values.Length, bytes);
    }

    public ExifEntry Long(ushort tag, params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            WriteU32(bytes.AsSpan(i * 4), values[i]);
        }

        return new ExifEntry(tag, 4, (uint)values.Length, bytes);
    }

    public ExifEntry Rational(ushort tag, params (uint Numerator, uint Denominator)[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            WriteU32(bytes.AsSpan(i * 8), values[i].Numerator);
            WriteU32(bytes.AsSpan((i * 8) + 4), values[i].Denominator);
        }

        return new ExifEntry(tag, 5, (uint)values.Length, bytes);
    }

    public ExifEntry Undefined(ushort tag, byte[] value) => new(tag, 7, (uint)value.Length, value);

    /// <summary>
    /// Builds the TIFF structure, starting with the byte order mark.
    /// </summary>
    public byte[] Build() => Build(trailing: null).Bytes;

    /// <summary>
    /// Builds a baseline, uncompressed 8-bit RGB TIFF image of the given size whose IFD0 also
    /// holds the configured entries and sub-directories. The pixel bytes are width times height
    /// times three, row by row.
    /// </summary>
    public byte[] BuildTiffImage(int width, int height, byte[] rgb)
    {
        var imageEntries = new List<ExifEntry>
        {
            Long(0x0100, (uint)width),
            Long(0x0101, (uint)height),
            Short(0x0102, 8, 8, 8),
            Short(0x0103, 1),
            Short(0x0106, 2),
            Long(0x0111, 0),
            Short(0x0115, 3),
            Long(0x0116, (uint)height),
            Long(0x0117, (uint)rgb.Length),
            Short(0x011C, 1),
        };
        var original = Ifd0.ToList();
        Ifd0.AddRange(imageEntries);

        // The strip offset is only known once the structure is laid out; it is a single LONG, so
        // the second build has the same size.
        var (_, length) = Build(trailing: rgb);
        Ifd0.RemoveAll(e => e.Tag == 0x0111);
        Ifd0.Add(Long(0x0111, (uint)length));
        var (bytes, _) = Build(trailing: rgb);

        Ifd0.Clear();
        Ifd0.AddRange(original);
        return bytes;
    }

    private (byte[] Bytes, int TrailingOffset) Build(byte[]? trailing)
    {
        var ifd0 = Ifd0.ToList();
        var exif = Exif.ToList();
        var interop = Interop.ToList();
        var gps = Gps.ToList();

        // Placeholders first, so every directory has its final entry count before layout.
        if (exif.Count > 0 || LoopExifPointerToIfd0)
        {
            ifd0.Add(Long(ExifPointerTag, 0));
        }

        if (gps.Count > 0)
        {
            ifd0.Add(Long(GpsPointerTag, 0));
        }

        if (interop.Count > 0)
        {
            exif.Add(Long(InteropPointerTag, 0));
        }

        List<ExifEntry>? ifd1 = null;
        if (Thumbnail is not null)
        {
            ifd1 = ThumbnailPointersAsShort
                ? [Short(0x0103, 6), Short(0x0201, 0), Short(0x0202, (ushort)Thumbnail.Length)]
                : [Short(0x0103, 6), Long(0x0201, 0), Long(0x0202, (uint)Thumbnail.Length)];
        }

        const int ifd0Offset = 8;
        var exifOffset = ifd0Offset + DirectorySize(ifd0);
        var interopOffset = exifOffset + (exif.Count > 0 ? DirectorySize(exif) : 0);
        var gpsOffset = interopOffset + (interop.Count > 0 ? DirectorySize(interop) : 0);
        var ifd1Offset = gpsOffset + (gps.Count > 0 ? DirectorySize(gps) : 0);
        var thumbnailOffset = ifd1Offset + (ifd1 is not null ? DirectorySize(ifd1) : 0);
        var trailingOffset = thumbnailOffset + (Thumbnail?.Length ?? 0);

        Replace(ifd0, ExifPointerTag, LoopExifPointerToIfd0 ? ifd0Offset : exifOffset);
        Replace(ifd0, GpsPointerTag, gpsOffset);
        Replace(exif, InteropPointerTag, interopOffset);
        if (ifd1 is not null)
        {
            Replace(ifd1, 0x0201, thumbnailOffset);
        }

        using var output = new MemoryStream();
        output.Write(BigEndian ? "MM"u8 : "II"u8);
        var header = new byte[6];
        WriteU16(header, 42);
        WriteU32(header.AsSpan(2), ifd0Offset);
        output.Write(header);

        WriteDirectory(output, ifd0, ifd1 is not null ? ifd1Offset : 0);
        if (exif.Count > 0)
        {
            WriteDirectory(output, exif, 0);
        }

        if (interop.Count > 0)
        {
            WriteDirectory(output, interop, 0);
        }

        if (gps.Count > 0)
        {
            WriteDirectory(output, gps, 0);
        }

        if (ifd1 is not null)
        {
            WriteDirectory(output, ifd1, 0);
            output.Write(Thumbnail);
        }

        if (trailing is not null)
        {
            output.Write(trailing);
        }

        return (output.ToArray(), trailingOffset);
    }

    private static int DirectorySize(List<ExifEntry> entries) =>
        2 + (entries.Count * 12) + 4 + entries.Where(e => e.Value.Length > 4).Sum(e => Align(e.Value.Length));

    private static int Align(int length) => length + (length & 1);

    private void Replace(List<ExifEntry> entries, ushort tag, int offset)
    {
        var index = entries.FindIndex(e => e.Tag == tag);
        if (index >= 0)
        {
            entries[index] = entries[index].Type == 3 ? Short(tag, (ushort)offset) : Long(tag, (uint)offset);
        }
    }

    private void WriteDirectory(MemoryStream output, List<ExifEntry> entries, int nextOffset)
    {
        var sorted = entries.OrderBy(e => e.Tag).ToList();
        var start = (int)output.Position;
        var dataOffset = start + 2 + (sorted.Count * 12) + 4;

        var directory = new byte[2 + (sorted.Count * 12) + 4];
        WriteU16(directory, (ushort)sorted.Count);
        using var data = new MemoryStream();
        for (var i = 0; i < sorted.Count; i++)
        {
            var entry = sorted[i];
            var slot = directory.AsSpan(2 + (i * 12));
            WriteU16(slot, entry.Tag);
            WriteU16(slot[2..], entry.Type);
            WriteU32(slot[4..], entry.Count);
            if (entry.Value.Length <= 4)
            {
                entry.Value.CopyTo(slot[8..]);
            }
            else
            {
                WriteU32(slot[8..], (uint)(dataOffset + data.Length));
                data.Write(entry.Value);
                if ((entry.Value.Length & 1) != 0)
                {
                    data.WriteByte(0);
                }
            }
        }

        WriteU32(directory.AsSpan(2 + (sorted.Count * 12)), (uint)nextOffset);
        output.Write(directory);
        output.Write(data.ToArray());
    }

    private void WriteU16(Span<byte> target, ushort value)
    {
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(target, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(target, value);
        }
    }

    private void WriteU32(Span<byte> target, uint value)
    {
        if (BigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(target, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(target, value);
        }
    }

    private void WriteU32(Span<byte> target, int value) => WriteU32(target, (uint)value);
}

/// <summary>
/// Container helpers that place metadata blocks into JPEG, PNG, WebP and GIF files.
/// </summary>
internal static class MetadataContainers
{
    public static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

    /// <summary>
    /// A JPEG marker segment: 0xFF, the marker, the big-endian length and the payload.
    /// </summary>
    public static byte[] JpegSegment(byte marker, params byte[][] payloadParts)
    {
        var payload = payloadParts.SelectMany(p => p).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }

    /// <summary>
    /// Inserts the given segments into a JPEG directly after its SOI marker and appends the
    /// given trailing bytes after its EOI marker.
    /// </summary>
    public static byte[] InsertIntoJpeg(byte[] jpeg, IEnumerable<byte[]> segments, byte[]? trailing = null)
    {
        using var output = new MemoryStream();
        output.Write(jpeg.AsSpan(0, 2));
        foreach (var segment in segments)
        {
            output.Write(segment);
        }

        output.Write(jpeg.AsSpan(2));
        if (trailing is not null)
        {
            output.Write(trailing);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Inserts the given chunks into a PNG directly after its IHDR chunk.
    /// </summary>
    public static byte[] InsertIntoPng(byte[] png, params (string Type, byte[] Data)[] chunks)
    {
        var ihdrEnd = 8 + 4 + 4 + 13 + 4;
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, ihdrEnd));
        foreach (var (type, data) in chunks)
        {
            SyntheticImages.WritePngChunk(output, type, data);
        }

        output.Write(png.AsSpan(ihdrEnd));
        return output.ToArray();
    }

    /// <summary>
    /// Wraps the image chunk of a simple WebP (VP8 or VP8L) into an extended WebP with a VP8X
    /// header and the given extra chunks. The VP8X flags announce ICC, EXIF and XMP when a chunk
    /// of that kind is present.
    /// </summary>
    public static byte[] ExtendedWebp(byte[] simpleWebp, int width, int height, params (string FourCc, byte[] Data)[] chunks)
    {
        var imageChunk = simpleWebp.AsSpan(12).ToArray();
        byte flags = 0;
        if (chunks.Any(c => c.FourCc == "ICCP"))
        {
            flags |= 0x20;
        }

        if (chunks.Any(c => c.FourCc == "EXIF"))
        {
            flags |= 0x08;
        }

        if (chunks.Any(c => c.FourCc == "XMP "))
        {
            flags |= 0x04;
        }

        var vp8x = new byte[10];
        vp8x[0] = flags;
        WriteU24(vp8x.AsSpan(4), width - 1);
        WriteU24(vp8x.AsSpan(7), height - 1);

        using var body = new MemoryStream();
        body.Write("WEBP"u8);
        WriteRiffChunk(body, "VP8X", vp8x);
        foreach (var (fourCc, data) in chunks.Where(c => c.FourCc == "ICCP"))
        {
            WriteRiffChunk(body, fourCc, data);
        }

        body.Write(imageChunk);
        foreach (var (fourCc, data) in chunks.Where(c => c.FourCc != "ICCP"))
        {
            WriteRiffChunk(body, fourCc, data);
        }

        using var output = new MemoryStream();
        output.Write("RIFF"u8);
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        output.Write(size);
        output.Write(body.ToArray());
        return output.ToArray();
    }

    /// <summary>
    /// Inserts the given extension blocks into a GIF directly before its first image descriptor
    /// or graphic control extension.
    /// </summary>
    public static byte[] InsertIntoGif(byte[] gif, params byte[][] blocks)
    {
        var position = 13;
        if ((gif[10] & 0x80) != 0)
        {
            position += 3 * (1 << ((gif[10] & 0x07) + 1));
        }

        using var output = new MemoryStream();
        output.Write(gif.AsSpan(0, position));
        foreach (var block in blocks)
        {
            output.Write(block);
        }

        output.Write(gif.AsSpan(position));
        return output.ToArray();
    }

    /// <summary>
    /// A GIF extension block with the given label whose data is split into sub-blocks.
    /// </summary>
    public static byte[] GifExtension(byte label, params byte[][] subBlocks)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x21);
        output.WriteByte(label);
        foreach (var subBlock in subBlocks)
        {
            for (var offset = 0; offset < subBlock.Length; offset += 255)
            {
                var length = Math.Min(255, subBlock.Length - offset);
                output.WriteByte((byte)length);
                output.Write(subBlock.AsSpan(offset, length));
            }
        }

        output.WriteByte(0);
        return output.ToArray();
    }

    /// <summary>
    /// A GIF application extension with the given 11-byte identifier and data.
    /// </summary>
    public static byte[] GifApplicationExtension(string identifier, byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x21);
        output.WriteByte(0xFF);
        output.WriteByte(11);
        output.Write(Encoding.ASCII.GetBytes(identifier));
        for (var offset = 0; offset < data.Length; offset += 255)
        {
            var length = Math.Min(255, data.Length - offset);
            output.WriteByte((byte)length);
            output.Write(data.AsSpan(offset, length));
        }

        output.WriteByte(0);
        return output.ToArray();
    }

    /// <summary>
    /// A GIF application extension holding an XMP packet the way the XMP specification stores it:
    /// the raw packet followed by the 258-byte magic trailer that ends any sub-block walk.
    /// </summary>
    public static byte[] GifXmpExtension(byte[] xmp)
    {
        using var output = new MemoryStream();
        output.Write([0x21, 0xFF, 11]);
        output.Write("XMP DataXMP"u8);
        output.Write(xmp);
        output.WriteByte(0x01);
        for (var value = 0xFF; value >= 0; value--)
        {
            output.WriteByte((byte)value);
        }

        output.WriteByte(0);
        return output.ToArray();
    }

    /// <summary>
    /// An XMP packet with GPS coordinates and the given marker text in its description.
    /// </summary>
    public static byte[] XmpWithGps(string marker) => Encoding.UTF8.GetBytes(
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:exif=\"http://ns.adobe.com/exif/1.0/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\""
        + " exif:GPSLatitude=\"52,31.0N\" exif:GPSLongitude=\"13,24.0E\">"
        + $"<dc:description>{marker}</dc:description></rdf:Description></rdf:RDF></x:xmpmeta>");

    private static void WriteRiffChunk(Stream output, string fourCc, byte[] data)
    {
        output.Write(Encoding.ASCII.GetBytes(fourCc));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        output.Write(size);
        output.Write(data);
        if ((data.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteU24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
