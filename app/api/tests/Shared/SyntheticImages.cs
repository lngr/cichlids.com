using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Cichlids.Tests.Shared;

/// <summary>
/// Image files assembled byte by byte for tests that need content an encoder would refuse to
/// produce, such as a PNG whose header declares far more pixels than its data holds.
/// </summary>
internal static class SyntheticImages
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// A greyscale PNG whose IHDR declares the given width and height. Its IDAT holds a few rows
    /// of zero pixels, so the file stays a few kilobytes however large the declared size is.
    /// </summary>
    public static byte[] PngDeclaring(int width, int height, int rows = 16)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[width + 1];
            for (var i = 0; i < rows; i++)
            {
                zlib.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WritePngChunk(png, "IHDR", header);
        WritePngChunk(png, "IDAT", raw.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// Writes one PNG chunk: length, type, data and the CRC over type and data.
    /// </summary>
    public static void WritePngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData.AsSpan(4));
        output.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        output.Write(crc);
    }

    /// <summary>
    /// The CRC-32 PNG uses for its chunks.
    /// </summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// The first bytes of an SVG document.
    /// </summary>
    public static byte[] Svg() =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" fill=\"red\"/></svg>"u8.ToArray();

    /// <summary>
    /// An ISO base media file header with the given major brand, for example avif or heic, as a
    /// HEIF loader recognises it.
    /// </summary>
    public static byte[] HeifHeader(string brand)
    {
        var bytes = new byte[28];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 28);
        Encoding.ASCII.GetBytes("ftyp", bytes.AsSpan(4));
        Encoding.ASCII.GetBytes(brand, bytes.AsSpan(8));
        Encoding.ASCII.GetBytes("mif1", bytes.AsSpan(16));
        Encoding.ASCII.GetBytes(brand, bytes.AsSpan(20));
        Encoding.ASCII.GetBytes("miaf", bytes.AsSpan(24));
        return bytes;
    }

    /// <summary>
    /// A minimal PDF document.
    /// </summary>
    public static byte[] Pdf() => "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    /// <summary>
    /// An uncompressed 24-bit BMP of the given size filled with one colour.
    /// </summary>
    public static byte[] Bmp(int width, int height, byte red = 200, byte green = 40, byte blue = 90)
    {
        var rowSize = ((width * 3) + 3) & ~3;
        var pixelBytes = rowSize * height;
        var bytes = new byte[54 + pixelBytes];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), pixelBytes);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = 54 + (y * rowSize) + (x * 3);
                bytes[offset] = blue;
                bytes[offset + 1] = green;
                bytes[offset + 2] = red;
            }
        }

        return bytes;
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
