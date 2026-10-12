using System.Buffers.Binary;
using System.Text;
using NetVips;

namespace Cichlids.Tests.Shared;

/// <summary>
/// Synthetic photos with the metadata a phone or camera writes: capture date, orientation,
/// camera model, artist, copyright and an ICC profile next to GPS coordinates, serial numbers,
/// the owner name, a maker note, XMP, IPTC, a comment, an MPF entry and a trailing secondary
/// image. Every value that must not survive sanitizing contains the text SECRET-, so a byte
/// search proves its removal.
/// </summary>
internal static class PhotoFixtures
{
    public const string Secret = "SECRET-";
    public const string CaptureDate = "2024:05:01 10:20:30";
    public const string Model = "FishCam X1";
    public const string Artist = "Jane Artist";
    public const string Copyright = "CC-BY Jane Artist";

    public static readonly byte[] SecretBytes = Encoding.ASCII.GetBytes(Secret);

    /// <summary>
    /// An EXIF block with kept and removed tags in IFD0, the Exif IFD and the GPS IFD, an Interop
    /// IFD and an IFD1 thumbnail.
    /// </summary>
    public static ExifBuilder PhoneExif(ushort orientation = 1, bool bigEndian = false)
    {
        var exif = new ExifBuilder(bigEndian);
        exif.Ifd0.AddRange(
        [
            exif.Ascii(0x010E, Secret + "DESCRIPTION"),
            exif.Ascii(0x010F, "FishCam"),
            exif.Ascii(0x0110, Model),
            exif.Short(0x0112, orientation),
            exif.Rational(0x011A, (72, 1)),
            exif.Rational(0x011B, (72, 1)),
            exif.Short(0x0128, 2),
            exif.Ascii(0x0131, "FishCam Firmware 1.0"),
            exif.Ascii(0x0132, CaptureDate),
            exif.Ascii(0x013B, Artist),
            exif.Ascii(0x013C, Secret + "HOST"),
            exif.Ascii(0x8298, Copyright),
            exif.Undefined(0x9C9C, Encoding.Unicode.GetBytes(Secret + "XPCOMMENT\0")),
        ]);
        exif.Exif.AddRange(
        [
            exif.Rational(0x829A, (1, 125)),
            exif.Rational(0x829D, (28, 10)),
            exif.Short(0x8827, 200),
            exif.Undefined(0x9000, "0232"u8.ToArray()),
            exif.Ascii(0x9003, CaptureDate),
            exif.Ascii(0x9004, CaptureDate),
            exif.Ascii(0x9011, "+02:00"),
            exif.Undefined(0x927C, Encoding.ASCII.GetBytes(Secret + "MAKERNOTE with a serial 0815")),
            exif.Undefined(0x9286, Encoding.ASCII.GetBytes("ASCII\0\0\0" + Secret + "USERCOMMENT")),
            exif.Short(0xA001, 1),
            exif.Ascii(0xA420, Secret + "UNIQUEID"),
            exif.Ascii(0xA430, Secret + "OWNER"),
            exif.Ascii(0xA431, Secret + "BODYSERIAL"),
            exif.Ascii(0xA434, "FishLens 24mm"),
            exif.Ascii(0xA435, Secret + "LENSSERIAL"),
        ]);
        exif.Interop.Add(exif.Ascii(0x0001, "R98"));
        exif.Gps.AddRange(
        [
            new ExifEntry(0x0000, 1, 4, [2, 3, 0, 0]),
            exif.Ascii(0x0001, "N"),
            exif.Rational(0x0002, (52, 1), (31, 1), (1234, 100)),
            exif.Ascii(0x0003, "E"),
            exif.Rational(0x0004, (13, 1), (24, 1), (5678, 100)),
            exif.Undefined(0x001B, Encoding.ASCII.GetBytes("ASCII\0\0\0" + Secret + "GPSMETHOD")),
        ]);
        exif.Thumbnail = SmallJpeg(Secret + "THUMBNAIL");
        return exif;
    }

    /// <summary>
    /// A JPEG of the given size with an embedded sRGB ICC profile and the full phone metadata:
    /// EXIF, XMP with GPS, IPTC in APP13, a comment, an MPF segment and a secondary image with
    /// its own GPS EXIF after the end of the primary image.
    /// </summary>
    public static byte[] PhoneJpeg(int width = 640, int height = 480, ushort orientation = 1, bool bigEndian = false)
    {
        var exif = PhoneExif(orientation, bigEndian).Build();
        var trailingExif = new ExifBuilder(bigEndian);
        trailingExif.Gps.Add(trailingExif.Ascii(0x0001, "N"));
        trailingExif.Ifd0.Add(trailingExif.Ascii(0x010E, Secret + "TRAILING"));
        var trailing = MetadataContainers.InsertIntoJpeg(
            SmallJpeg(comment: null),
            [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, trailingExif.Build())]);

        return MetadataContainers.InsertIntoJpeg(
            BaseJpeg(width, height),
            [
                MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, exif),
                MetadataContainers.JpegSegment(0xE1, "http://ns.adobe.com/xap/1.0/\0"u8.ToArray(), MetadataContainers.XmpWithGps(Secret + "XMP")),
                MetadataContainers.JpegSegment(0xE2, "MPF\0"u8.ToArray(), Encoding.ASCII.GetBytes(Secret + "MPF")),
                MetadataContainers.JpegSegment(0xED, "Photoshop 3.0\0"u8.ToArray(), PhotoshopIptc(Secret + "IPTC")),
                MetadataContainers.JpegSegment(0xFE, Encoding.ASCII.GetBytes(Secret + "COMMENT")),
            ],
            trailing);
    }

    /// <summary>
    /// A baseline JPEG of flat colour with an embedded sRGB ICC profile and no other metadata.
    /// </summary>
    public static byte[] BaseJpeg(int width, int height)
    {
        using var image = WithIcc(Flat(width, height));
        return image.WriteToBuffer(".jpg", new VOption { { "Q", 90 }, { "keep", Enums.ForeignKeep.Icc } });
    }

    /// <summary>
    /// A flat-colour 8-bit sRGB image tagged with the built-in sRGB ICC profile.
    /// </summary>
    public static Image WithIcc(Image image) =>
        image.Mutate(m => m.Set(GValue.BlobType, "icc-profile-data", IccProfile("srgb")));

    public static Image Flat(int width, int height, int red = 70, int green = 130, int blue = 180) =>
        (Image.Black(width, height) + new[] { red, green, blue }).Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);

    /// <summary>
    /// The bytes of a libvips built-in ICC profile, such as srgb or p3.
    /// </summary>
    public static byte[] IccProfile(string name)
    {
        using var probe = Flat(1, 1).IccTransform(name, inputProfile: "srgb");
        return (byte[])probe.Get("icc-profile-data");
    }

    /// <summary>
    /// A tiny JPEG, optionally with a comment segment.
    /// </summary>
    public static byte[] SmallJpeg(string? comment)
    {
        using var image = Flat(8, 8);
        var jpeg = image.WriteToBuffer(".jpg", new VOption { { "keep", Enums.ForeignKeep.None } });
        return comment is null
            ? jpeg
            : MetadataContainers.InsertIntoJpeg(jpeg, [MetadataContainers.JpegSegment(0xFE, Encoding.ASCII.GetBytes(comment))]);
    }

    /// <summary>
    /// A little-endian TIFF of 4 x 3 pixels with GPS and serial number tags whose compression
    /// value is unknown, so its pixels do not decode.
    /// </summary>
    public static byte[] UndecodableTiffWithGps()
    {
        var builder = PhoneExif();
        builder.Thumbnail = null;
        builder.Exif.Clear();
        builder.Interop.Clear();
        var tiff = builder.BuildTiffImage(4, 3, new byte[4 * 3 * 3]);

        const int firstIfd = 8;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(firstIfd));
        for (var index = 0; index < count; index++)
        {
            var entry = firstIfd + 2 + (index * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry)) == 0x0103)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(entry + 8), 0x7FFF);
                return tiff;
            }
        }

        throw new InvalidOperationException("The TIFF has no compression entry.");
    }

    /// <summary>
    /// Returns whether the bytes contain the SECRET- marker anywhere.
    /// </summary>
    public static bool ContainsSecret(byte[] bytes) => bytes.AsSpan().IndexOf(SecretBytes) >= 0;

    // A Photoshop image resource block with one IPTC-NAA resource holding a caption record.
    private static byte[] PhotoshopIptc(string caption)
    {
        var text = Encoding.ASCII.GetBytes(caption);
        var iptc = new List<byte> { 0x1C, 0x02, 0x78, (byte)(text.Length >> 8), (byte)text.Length };
        iptc.AddRange(text);
        if ((iptc.Count & 1) != 0)
        {
            iptc.Add(0);
        }

        var block = new List<byte>();
        block.AddRange("8BIM"u8.ToArray());
        block.AddRange([0x04, 0x04, 0x00, 0x00]);
        block.AddRange([(byte)(iptc.Count >> 24), (byte)(iptc.Count >> 16), (byte)(iptc.Count >> 8), (byte)iptc.Count]);
        block.AddRange(iptc);
        return block.ToArray();
    }
}
