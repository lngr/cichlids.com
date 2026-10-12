using System.Buffers.Binary;
using System.Text;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;
using NetVips;
using Directory = MetadataExtractor.Directory;

namespace Cichlids.Infrastructure.Tests;

[Collection(LibvipsCollection.Name)]
public sealed class MediaMetadataSanitizerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removes_location_serial_and_owner_data_from_a_jpeg(bool bigEndian)
    {
        var source = PhotoFixtures.PhoneJpeg(bigEndian: bigEndian);
        var before = Read(source);
        Assert.NotEmpty(before.OfType<GpsDirectory>());
        Assert.NotEmpty(before.OfType<XmpDirectory>());
        Assert.NotEmpty(before.OfType<IptcDirectory>());
        Assert.NotEmpty(before.OfType<JpegCommentDirectory>());
        Assert.NotEmpty(before.OfType<ExifThumbnailDirectory>());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.Empty(after.OfType<XmpDirectory>());
        Assert.Empty(after.OfType<IptcDirectory>());
        Assert.Empty(after.OfType<JpegCommentDirectory>());
        Assert.Empty(after.OfType<ExifThumbnailDirectory>());

        var subIfd = Assert.Single(after.OfType<ExifSubIfdDirectory>());
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagBodySerialNumber));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagLensSerialNumber));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagCameraOwnerName));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagMakernote));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagImageUniqueId));
        Assert.False(subIfd.ContainsTag(ExifDirectoryBase.TagUserComment));
        Assert.DoesNotContain(after, d => d.Name.Contains("Makernote", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keeps_capture_date_orientation_colour_profile_camera_model_artist_and_copyright_of_a_jpeg(bool bigEndian)
    {
        var sanitized = MediaMetadataSanitizer.Sanitize(PhotoFixtures.PhoneJpeg(orientation: 6, bigEndian: bigEndian));

        var after = Read(sanitized);
        var ifd0 = Assert.Single(after.OfType<ExifIfd0Directory>());
        Assert.Equal(6, ifd0.GetInt32(ExifDirectoryBase.TagOrientation));
        Assert.Equal(PhotoFixtures.Model, ifd0.GetString(ExifDirectoryBase.TagModel));
        Assert.Equal(PhotoFixtures.Artist, ifd0.GetString(ExifDirectoryBase.TagArtist));
        Assert.Equal(PhotoFixtures.Copyright, ifd0.GetString(ExifDirectoryBase.TagCopyright));
        var subIfd = Assert.Single(after.OfType<ExifSubIfdDirectory>());
        Assert.Equal(PhotoFixtures.CaptureDate, subIfd.GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Equal("FishLens 24mm", subIfd.GetString(ExifDirectoryBase.TagLensModel));
        Assert.Single(after.OfType<ExifInteropDirectory>());
        Assert.NotEmpty(after.OfType<IccDirectory>());
    }

    [Fact]
    public void Keeps_the_colour_profile_stored_in_the_exif_ifd0()
    {
        var profile = PhotoFixtures.IccProfile("p3");
        var builder = PhotoFixtures.PhoneExif();
        builder.Ifd0.Add(builder.Undefined(0x8773, profile));

        var source = JpegWithExif(builder.Build());
        Assert.Equal(2, Read(source).OfType<IccDirectory>().Count());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.True(sanitized.AsSpan().IndexOf(profile) > 0, "The colour profile was removed.");

        // The reader parses the IFD0 profile into its own ICC directory next to the APP2 profile.
        Assert.Equal(2, Read(sanitized).OfType<IccDirectory>().Count());
    }

    [Fact]
    public void Leaves_the_jpeg_bytes_from_the_frame_header_to_the_end_of_image_untouched()
    {
        // The metadata segments hold a thumbnail with a frame header of its own, so the primary
        // frame is taken from the JPEG the fixture was built on.
        var frame = FrameToEndOfImage(PhotoFixtures.BaseJpeg(640, 480));
        var source = PhotoFixtures.PhoneJpeg(640, 480);
        Assert.True(source.AsSpan().IndexOf(frame) > 0);

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        var position = sanitized.AsSpan().IndexOf(frame);
        Assert.True(position > 0, "The frame, tables or scan data changed.");
        Assert.Equal(sanitized.Length, position + frame.Length);
    }

    [Fact]
    public void Is_idempotent()
    {
        var once = MediaMetadataSanitizer.Sanitize(PhotoFixtures.PhoneJpeg());

        var twice = MediaMetadataSanitizer.Sanitize(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Drops_a_truncated_exif_block_without_throwing()
    {
        var exif = PhotoFixtures.PhoneExif().Build();
        var source = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48),
            [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, exif[..60])]);

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.Empty(Read(sanitized).OfType<ExifDirectoryBase>());
        Assert.False(PhotoFixtures.ContainsSecret(sanitized));
        Assert.NotEmpty(Read(sanitized).OfType<IccDirectory>());
        using var decoded = Image.NewFromBuffer(sanitized);
        Assert.Equal(64, decoded.Width);
    }

    [Fact]
    public void Drops_an_exif_block_whose_directories_loop_without_throwing()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Exif.Clear();
        builder.LoopExifPointerToIfd0 = true;
        var source = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48),
            [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, builder.Build())]);

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized));
        Assert.Empty(Read(sanitized).OfType<ExifDirectoryBase>());
    }

    [Fact]
    public void Keeps_the_exif_directory_when_a_removed_value_overlaps_it()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Ifd0.Add(builder.Undefined(0xC4A5, Encoding.ASCII.GetBytes(PhotoFixtures.Secret + new string('P', 120))));
        var block = builder.Build();
        var exifOffset = ReadEntryValue(block, 8, ExifBuilder.ExifPointerTag);
        SetEntryValue(block, 8, 0xC4A5, exifOffset - 40);

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(block));

        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(Read(sanitized).OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Empty(Read(sanitized).OfType<GpsDirectory>());
    }

    [Fact]
    public void Keeps_the_exif_block_when_the_thumbnail_runs_past_its_end_or_the_directory_chain_loops()
    {
        var builder = PhotoFixtures.PhoneExif();
        var block = builder.Build();
        var ifd1Offset = ReadNextIfd(block, 8);
        SetEntryValue(block, ifd1Offset, 0x0202, (uint)block.Length);
        SetNextIfd(block, ifd1Offset, ifd1Offset);

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(block));

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Empty(after.OfType<ExifThumbnailDirectory>());
    }

    [Fact]
    public void Removes_the_gps_directory_when_the_interop_pointer_leads_to_it()
    {
        // GPSLatitudeRef and GPSLatitude share their tag numbers with InteroperabilityIndex and
        // InteroperabilityVersion.
        var block = PhotoFixtures.PhoneExif().Build();
        var exifOffset = ReadEntryValue(block, 8, ExifBuilder.ExifPointerTag);
        var gpsOffset = ReadEntryValue(block, 8, ExifBuilder.GpsPointerTag);
        SetEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag, gpsOffset);
        byte[] latitude = [52, 0, 0, 0, 1, 0, 0, 0, 31, 0, 0, 0, 1, 0, 0, 0];
        Assert.True(block.AsSpan().IndexOf(latitude) > 0);

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(block));

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.True(sanitized.AsSpan().IndexOf(latitude) < 0, "The GPS latitude survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.DoesNotContain(after.SelectMany(d => d.Tags), t => t.Name.Contains("GPS", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    [Fact]
    public void Removes_interop_entries_whose_type_does_not_match_their_tag()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Interop.Clear();
        builder.Interop.Add(builder.Ascii(0x0001, "N"));
        builder.Interop.Add(builder.Rational(0x0002, (52, 1), (31, 1), (1234, 100)));
        builder.Interop.Add(builder.Ascii(0x1000, "FishCam.jpg"));
        builder.Interop.Add(builder.Long(0x1001, 640));
        var block = builder.Build();
        byte[] latitude = [52, 0, 0, 0, 1, 0, 0, 0, 31, 0, 0, 0, 1, 0, 0, 0];

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(block));

        Assert.True(sanitized.AsSpan().IndexOf(latitude) < 0, "The rational interop value survived.");
        var interop = Assert.Single(Read(sanitized).OfType<ExifInteropDirectory>());
        Assert.False(interop.ContainsTag(0x0001));
        Assert.False(interop.ContainsTag(0x0002));
        Assert.Equal(640, interop.GetInt32(0x1001));
    }

    [Fact]
    public void Removes_the_gps_style_directory_behind_the_first_of_two_interop_pointers()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Exif.Add(builder.Long(ExifBuilder.InteropPointerTag, 0));
        var block = builder.Build();
        var exifOffset = ReadEntryValue(block, 8, ExifBuilder.ExifPointerTag);
        var interopOffset = ReadEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag);
        (block, var gpsStyleOffset) = AppendGpsStyleDirectory(block);
        SetEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag, gpsStyleOffset, occurrence: 0);
        SetEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag, interopOffset, occurrence: 1);

        AssertGpsStyleDirectoryRemoved(block);
    }

    [Fact]
    public void Removes_the_gps_style_directory_behind_the_interop_pointer_of_the_first_of_two_exif_pointers()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Ifd0.Add(builder.Long(ExifBuilder.ExifPointerTag, 0));
        var block = builder.Build();
        var exifOffset = ReadEntryValue(block, 8, ExifBuilder.ExifPointerTag);
        var interopOffset = ReadEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag);
        (block, var gpsStyleOffset) = AppendGpsStyleDirectory(block);
        (block, var secondExifOffset) = AppendDirectory(block, builder.Long(ExifBuilder.InteropPointerTag, interopOffset));
        SetEntryValue(block, exifOffset, ExifBuilder.InteropPointerTag, gpsStyleOffset);
        SetEntryValue(block, 8, ExifBuilder.ExifPointerTag, secondExifOffset, occurrence: 1);

        AssertGpsStyleDirectoryRemoved(block);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Keeps_the_related_image_file_format_only_up_to_64_bytes(int count, bool kept)
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Interop.Clear();
        builder.Interop.Add(builder.Ascii(0x1000, PhotoFixtures.Secret.PadRight(count - 1, 'x')));
        builder.Interop.Add(builder.Long(0x1001, 640));

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(builder.Build()));

        var interop = Assert.Single(Read(sanitized).OfType<ExifInteropDirectory>());
        Assert.Equal(kept, interop.ContainsTag(0x1000));
        Assert.Equal(kept, PhotoFixtures.ContainsSecret(sanitized));
        Assert.Equal(640, interop.GetInt32(0x1001));
    }

    [Fact]
    public void Removes_text_chunks_and_scrubs_the_exif_chunk_of_a_png()
    {
        using var image = PhotoFixtures.WithIcc(PhotoFixtures.Flat(64, 48));
        var basePng = image.WriteToBuffer(".png", new VOption { { "keep", Enums.ForeignKeep.Icc } });
        var source = MetadataContainers.InsertIntoPng(
            basePng,
            ("eXIf", PhotoFixtures.PhoneExif(orientation: 3).Build()),
            ("tEXt", Encoding.Latin1.GetBytes("Comment\0" + PhotoFixtures.Secret + "TEXT")),
            ("zTXt", [.. "Comment\0\0"u8.ToArray(), .. Compress(PhotoFixtures.Secret + "ZTXT")]),
            ("iTXt", [.. "XML:com.adobe.xmp\0\0\0\0\0"u8.ToArray(), .. MetadataContainers.XmpWithGps(PhotoFixtures.Secret + "ITXT")]),
            ("tIME", [0x07, 0xE8, 5, 1, 10, 20, 30]));
        var before = Read(source);
        Assert.NotEmpty(before.OfType<GpsDirectory>());
        Assert.NotEmpty(before.OfType<XmpDirectory>());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.Empty(after.OfType<XmpDirectory>());
        Assert.DoesNotContain(after.OfType<PngDirectory>(), d => d.GetPngChunkType() == PngChunkType.tIME);
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Equal(3, Assert.Single(after.OfType<ExifIfd0Directory>()).GetInt32(ExifDirectoryBase.TagOrientation));
        Assert.Contains(after.OfType<PngDirectory>(), d => d.GetPngChunkType() == PngChunkType.iCCP);
        AssertSamePixels(source, sanitized);
    }

    [Fact]
    public void Removes_the_xmp_chunk_and_scrubs_the_exif_chunk_of_a_webp()
    {
        using var image = PhotoFixtures.Flat(64, 48);
        var simple = image.WriteToBuffer(".webp", new VOption { { "lossless", true }, { "keep", Enums.ForeignKeep.None } });
        var source = MetadataContainers.ExtendedWebp(
            simple,
            64,
            48,
            ("ICCP", PhotoFixtures.IccProfile("srgb")),
            ("EXIF", PhotoFixtures.PhoneExif().Build()),
            ("XMP ", MetadataContainers.XmpWithGps(PhotoFixtures.Secret + "XMP")));
        var before = Read(source);
        Assert.NotEmpty(before.OfType<GpsDirectory>());
        Assert.NotEmpty(before.OfType<XmpDirectory>());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.Empty(after.OfType<XmpDirectory>());
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.NotEmpty(after.OfType<IccDirectory>());
        Assert.Equal(sanitized.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(sanitized.AsSpan(4)));
        var vp8xFlags = sanitized[20];
        Assert.Equal(0, vp8xFlags & 0x04);
        Assert.NotEqual(0, vp8xFlags & 0x08);
        Assert.NotEqual(0, vp8xFlags & 0x20);
        AssertSamePixels(source, sanitized);
    }

    [Fact]
    public void Removes_comments_and_xmp_from_a_gif_and_keeps_the_looping_extension()
    {
        using var image = PhotoFixtures.Flat(32, 24);
        var baseGif = image.WriteToBuffer(".gif");
        var source = MetadataContainers.InsertIntoGif(
            baseGif,
            MetadataContainers.GifApplicationExtension("NETSCAPE2.0", [1, 0, 0]),
            MetadataContainers.GifExtension(0xFE, Encoding.ASCII.GetBytes(PhotoFixtures.Secret + "GIFCOMMENT")),
            MetadataContainers.GifXmpExtension(MetadataContainers.XmpWithGps(PhotoFixtures.Secret + "GIFXMP")));
        Assert.NotEmpty(Read(source).OfType<GifCommentDirectory>());
        Assert.NotEmpty(Read(source).OfType<XmpDirectory>());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GifCommentDirectory>());
        Assert.Empty(after.OfType<XmpDirectory>());
        Assert.True(sanitized.AsSpan().IndexOf("NETSCAPE2.0"u8) > 0);
        AssertSamePixels(source, sanitized);
    }

    [Fact]
    public void Reencodes_a_tiff_to_identical_pixels_without_gps()
    {
        const int width = 40;
        const int height = 30;
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 7);
        }

        var builder = PhotoFixtures.PhoneExif();
        builder.Thumbnail = null;
        builder.Exif.Clear();
        builder.Interop.Clear();
        var source = builder.BuildTiffImage(width, height, rgb);
        Assert.NotEmpty(Read(source).OfType<GpsDirectory>());

        var sanitized = MediaMetadataSanitizer.Sanitize(source);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.Empty(Read(sanitized).OfType<GpsDirectory>());
        AssertSamePixels(source, sanitized);
    }

    [Fact]
    public void Refuses_a_tiff_that_does_not_decode()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Thumbnail = null;
        builder.Exif.Clear();
        builder.Interop.Clear();
        var source = builder.BuildTiffImage(4, 3, new byte[4 * 3 * 3]);
        SetEntryValue(source, 8, 0x0103, 0x7FFF);
        Assert.NotEmpty(Read(source).OfType<GpsDirectory>());

        Assert.Throws<MediaMetadataSanitizer.UnsanitizableImageException>(() => MediaMetadataSanitizer.Sanitize(source));
    }

    [Fact]
    public void Returns_bmp_and_unknown_bytes_unchanged()
    {
        var bmp = SyntheticImages.Bmp(5, 4);
        var unknown = "not an image at all"u8.ToArray();

        Assert.Equal(bmp, MediaMetadataSanitizer.Sanitize(bmp));
        Assert.Equal(unknown, MediaMetadataSanitizer.Sanitize(unknown));
        Assert.Empty(MediaMetadataSanitizer.Sanitize([]));
    }

    private static byte[] JpegWithExif(byte[] block) =>
        MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, block)]);

    // Latitude 47 degrees 11 minutes of the directory AppendGpsStyleDirectory writes.
    private static readonly byte[] GpsStyleLatitude = [47, 0, 0, 0, 1, 0, 0, 0, 11, 0, 0, 0, 1, 0, 0, 0];

    // GPSLatitudeRef and GPSLatitude share their tag numbers with InteroperabilityIndex and
    // InteroperabilityVersion, so a directory reached as Interop IFD can hold a location.
    private static (byte[] Block, uint Offset) AppendGpsStyleDirectory(byte[] block)
    {
        var builder = new ExifBuilder();
        return AppendDirectory(
            block,
            builder.Ascii(0x0001, "N"),
            builder.Rational(0x0002, (47, 1), (11, 1), (2222, 100)),
            builder.Undefined(0x001B, Encoding.ASCII.GetBytes("ASCII\0\0\0" + PhotoFixtures.Secret + "APPENDEDGPS")));
    }

    private static void AssertGpsStyleDirectoryRemoved(byte[] block)
    {
        Assert.True(PhotoFixtures.ContainsSecret(block));
        Assert.True(block.AsSpan().IndexOf(GpsStyleLatitude) > 0);

        var sanitized = MediaMetadataSanitizer.Sanitize(JpegWithExif(block));

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.True(sanitized.AsSpan().IndexOf(GpsStyleLatitude) < 0, "The GPS latitude survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.DoesNotContain(after.SelectMany(d => d.Tags), t => t.Name.Contains("GPS", StringComparison.OrdinalIgnoreCase));

        // A block that exhausts its work budget is dropped whole; the kept capture date shows that
        // the block was scrubbed within it.
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    // The helpers below read and patch little-endian blocks built by ExifBuilder.

    // Appends a directory with the given entries and no next directory at an even offset after
    // the block; values longer than four bytes follow the directory.
    private static (byte[] Block, uint Offset) AppendDirectory(byte[] block, params ExifEntry[] entries)
    {
        var offset = block.Length + (block.Length & 1);
        var valueOffset = offset + 2 + (entries.Length * 12) + 4;
        var output = new byte[valueOffset + entries.Where(e => e.Value.Length > 4).Sum(e => e.Value.Length + (e.Value.Length & 1))];
        block.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), (ushort)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var (tag, type, count, value) = entries[i];
            var slot = output.AsSpan(offset + 2 + (i * 12));
            BinaryPrimitives.WriteUInt16LittleEndian(slot, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(slot[2..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[4..], count);
            if (value.Length <= 4)
            {
                value.CopyTo(slot[8..]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(slot[8..], (uint)valueOffset);
                value.CopyTo(output, valueOffset);
                valueOffset += value.Length + (value.Length & 1);
            }
        }

        return (output, (uint)offset);
    }

    // Returns the position of the given occurrence of the tag in the directory.
    private static int FindEntry(byte[] block, uint ifdOffset, ushort tag, int occurrence = 0)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan((int)ifdOffset));
        for (var i = 0; i < count; i++)
        {
            var position = (int)ifdOffset + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(position)) == tag && occurrence-- == 0)
            {
                return position;
            }
        }

        throw new InvalidOperationException($"Tag {tag:X4} not found.");
    }

    private static uint ReadEntryValue(byte[] block, uint ifdOffset, ushort tag) =>
        BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(FindEntry(block, ifdOffset, tag) + 8));

    private static void SetEntryValue(byte[] block, uint ifdOffset, ushort tag, uint value, int occurrence = 0) =>
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(FindEntry(block, ifdOffset, tag, occurrence) + 8), value);

    private static uint ReadNextIfd(byte[] block, uint ifdOffset)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan((int)ifdOffset));
        return BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan((int)ifdOffset + 2 + (count * 12)));
    }

    private static void SetNextIfd(byte[] block, uint ifdOffset, uint next)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan((int)ifdOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan((int)ifdOffset + 2 + (count * 12)), next);
    }

    private static IReadOnlyList<Directory> Read(byte[] bytes) => ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));

    // The bytes from the first SOF0 marker up to and including the first EOI marker after it.
    private static byte[] FrameToEndOfImage(byte[] jpeg)
    {
        var start = jpeg.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        var end = jpeg.AsSpan(start).IndexOf([(byte)0xFF, (byte)0xD9]) + start + 2;
        return jpeg[start..end];
    }

    private static void AssertSamePixels(byte[] expected, byte[] actual)
    {
        using var a = Image.NewFromBuffer(expected);
        using var b = Image.NewFromBuffer(actual);
        Assert.Equal((a.Width, a.Height, a.Bands), (b.Width, b.Height, b.Bands));
        using var difference = (a - b).Abs();
        Assert.Equal(0, difference.Max());
    }

    private static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.Latin1.GetBytes(text));
        }

        return output.ToArray();
    }
}
