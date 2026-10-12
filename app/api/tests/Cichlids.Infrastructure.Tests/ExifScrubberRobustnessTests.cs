using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using NetVips;

namespace Cichlids.Infrastructure.Tests;

/// <summary>
/// Crafted EXIF blocks that try to exhaust the stack or the CPU of the metadata sanitizer.
/// </summary>
[Collection(LibvipsCollection.Name)]
public sealed class ExifScrubberRobustnessTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    [Fact]
    public void Sanitizes_a_jpeg_whose_exif_holds_a_64_kilobyte_chain_of_directories_on_a_small_stack()
    {
        var block = WithDirectoryChain(PhoneExifWithoutThumbnail(), 65_527);
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, block)]);

        var sanitized = RunTimedOnSmallStack(() => MediaMetadataSanitizer.Sanitize(jpeg));

        AssertNoLocation(sanitized);
    }

    [Fact]
    public void Sanitizes_a_webp_whose_exif_chunk_holds_a_multi_megabyte_chain_of_directories_on_a_small_stack()
    {
        var block = WithDirectoryChain(PhoneExifWithoutThumbnail(), 4 * 1024 * 1024);
        using var image = PhotoFixtures.Flat(64, 48);
        var simple = image.WriteToBuffer(".webp", new VOption { { "lossless", true }, { "keep", Enums.ForeignKeep.None } });
        var webp = MetadataContainers.ExtendedWebp(simple, 64, 48, ("EXIF", block));

        var sanitized = RunTimedOnSmallStack(() => MediaMetadataSanitizer.Sanitize(webp));

        AssertNoLocation(sanitized);
    }

    [Fact]
    public void Sanitizes_a_png_whose_exif_chunk_holds_a_multi_megabyte_chain_of_directories_on_a_small_stack()
    {
        var block = WithDirectoryChain(PhoneExifWithoutThumbnail(), 4 * 1024 * 1024);
        using var image = PhotoFixtures.Flat(64, 48);
        var png = MetadataContainers.InsertIntoPng(image.WriteToBuffer(".png"), ("eXIf", block));

        var sanitized = RunTimedOnSmallStack(() => MediaMetadataSanitizer.Sanitize(png));

        AssertNoLocation(sanitized);
    }

    [Fact]
    public void Bounds_the_work_for_a_removed_sub_ifd_entry_that_declares_four_billion_pointers()
    {
        // The pointers sit in a zero-filled tail, so every pointer read inside the block is a null
        // pointer and the declared count alone decides the work.
        var builder = PhotoFixtures.PhoneExif();
        builder.Thumbnail = null;
        builder.Ifd0.Add(new ExifEntry(0x014A, 4, uint.MaxValue, [0, 0, 0, 0]));
        var head = builder.Build();
        var block = new byte[head.Length + 4096];
        head.CopyTo(block, 0);
        var ifd0Count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(8));
        for (var i = 0; i < ifd0Count; i++)
        {
            var entry = 8 + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(entry)) == 0x014A)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(entry + 8), (uint)head.Length);
            }
        }

        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, block)]);

        var sanitized = RunTimedOnSmallStack(() => MediaMetadataSanitizer.Sanitize(jpeg));

        AssertNoLocation(sanitized);
    }

    [Fact]
    public void Bounds_time_and_allocation_for_thousands_of_removed_sub_ifd_entries()
    {
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, SubIfdBomb())]);

        var (sanitized, allocated) = RunMeasuredOnSmallStack(() => MediaMetadataSanitizer.Sanitize(jpeg));

        AssertNoLocation(sanitized);
        Assert.True(allocated < 4L * 1024 * 1024, $"Sanitizing allocated {allocated} bytes.");
    }

    [Fact]
    public void Bounds_time_and_allocation_for_a_jpeg_with_hundreds_of_exif_segments()
    {
        var bomb = MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, SubIfdBomb());
        var jpeg = MetadataContainers.InsertIntoJpeg(PhotoFixtures.BaseJpeg(64, 48), Enumerable.Repeat(bomb, 400));
        Assert.True(jpeg.Length > 25 * 1000 * 1000, $"The test file has {jpeg.Length} bytes.");

        var (sanitized, allocated) = RunMeasuredOnSmallStack(() => MediaMetadataSanitizer.Sanitize(jpeg));

        AssertNoLocation(sanitized);
        Assert.True(allocated < 4L * 1024 * 1024, $"Sanitizing allocated {allocated} bytes.");
    }

    [Fact]
    public void Keeps_only_the_first_exif_segment_of_a_jpeg()
    {
        var first = PhotoFixtures.PhoneExif(orientation: 6).Build();
        var second = PhotoFixtures.PhoneExif(orientation: 3).Build();
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48),
            [
                MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, first),
                MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, second),
            ]);

        var sanitized = MediaMetadataSanitizer.Sanitize(jpeg);

        Assert.Equal(1, Occurrences(sanitized, MetadataContainers.ExifHeader));
        Assert.Equal(6, Read(sanitized).OfType<ExifIfd0Directory>().First().GetInt32(ExifDirectoryBase.TagOrientation));
    }

    [Fact]
    public void Bounds_the_work_for_thousands_of_entries_whose_values_cover_the_whole_block()
    {
        // Every kept Make entry and every removed ImageDescription entry claims a value of
        // 60000 bytes at offset 8, so per-byte work per entry would be quadratic.
        var builder = new ExifBuilder();
        for (var i = 0; i < 2700; i++)
        {
            builder.Ifd0.Add(new ExifEntry(0x010F, 2, 60_000, [8, 0, 0, 0]));
            builder.Ifd0.Add(new ExifEntry(0x010E, 2, 60_000, [8, 0, 0, 0]));
        }

        builder.Gps.Add(builder.Ascii(0x0001, "N"));
        var block = builder.Build();
        Assert.True(block.Length < 65_527, $"The block has {block.Length} bytes.");
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, block)]);

        var sanitized = RunTimedOnSmallStack(() => MediaMetadataSanitizer.Sanitize(jpeg));

        Assert.Empty(Read(sanitized).OfType<GpsDirectory>());
    }

    [Fact]
    public void Removes_a_maker_note_placed_in_the_interop_directory_and_keeps_the_interop_index()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Interop.Add(builder.Undefined(0x927C, Encoding.ASCII.GetBytes(PhotoFixtures.Secret + "INTEROP-MAKERNOTE")));
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, builder.Build())]);

        var sanitized = MediaMetadataSanitizer.Sanitize(jpeg);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.Equal("R98", Assert.Single(Read(sanitized).OfType<ExifInteropDirectory>()).GetString(0x0001));
    }

    [Fact]
    public void Removes_entries_of_unknown_type_and_keeps_the_rest_of_the_block()
    {
        // Seen in legacy files: an Interop pointer into thumbnail data, so the Interop directory
        // is read from bytes whose entries have arbitrary types.
        var builder = PhotoFixtures.PhoneExif();
        builder.Ifd0.Add(new ExifEntry(0x0123, 0x4747, 18_247, [1, 2, 3, 4]));
        builder.Interop.Add(new ExifEntry(0x5137, 0x2828, 20_026, [5, 6, 7, 8]));
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, builder.Build())]);

        var sanitized = MediaMetadataSanitizer.Sanitize(jpeg);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        var after = Read(sanitized);
        Assert.Empty(after.OfType<GpsDirectory>());
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(after.OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    [Fact]
    public void Zeroes_a_thumbnail_whose_big_endian_offset_and_length_are_short_values()
    {
        var builder = PhotoFixtures.PhoneExif(bigEndian: true);
        builder.ThumbnailPointersAsShort = true;
        var jpeg = MetadataContainers.InsertIntoJpeg(
            PhotoFixtures.BaseJpeg(64, 48), [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, builder.Build())]);

        var sanitized = MediaMetadataSanitizer.Sanitize(jpeg);

        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.Equal(PhotoFixtures.CaptureDate, Assert.Single(Read(sanitized).OfType<ExifSubIfdDirectory>()).GetString(ExifDirectoryBase.TagDateTimeOriginal));
    }

    // A 64 KiB block whose IFD0 holds 5000 removed SubIFDs entries, each declaring 16381
    // pointers at offset 8, next to the phone metadata with GPS.
    private static byte[] SubIfdBomb()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Thumbnail = null;
        for (var i = 0; i < 5000; i++)
        {
            builder.Ifd0.Add(new ExifEntry(0x014A, 4, 16_381, [8, 0, 0, 0]));
        }

        var head = builder.Build();
        Assert.True(head.Length <= 65_527, $"The block has {head.Length} bytes.");
        var block = new byte[65_527];
        head.CopyTo(block, 0);
        return block;
    }

    private static int Occurrences(byte[] bytes, byte[] pattern)
    {
        var count = 0;
        var span = bytes.AsSpan();
        int index;
        while ((index = span.IndexOf(pattern)) >= 0)
        {
            count++;
            span = span[(index + pattern.Length)..];
        }

        return count;
    }

    private static (T Result, long Allocated) RunMeasuredOnSmallStack<T>(Func<T> action)
    {
        long allocated = 0;
        var result = RunTimedOnSmallStack(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var value = action();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return value;
        });
        return (result, allocated);
    }

    private static ExifBuilder PhoneExifWithoutThumbnail()
    {
        var builder = PhotoFixtures.PhoneExif();
        builder.Thumbnail = null;
        return builder;
    }

    // Builds the block and points the next-directory field of IFD0 at a chain of empty
    // directories (a zero entry count and a pointer to the following one) that fills the block
    // up to the given length.
    private static byte[] WithDirectoryChain(ExifBuilder builder, int length)
    {
        var head = builder.Build();
        var block = new byte[length];
        head.CopyTo(block, 0);
        var ifd0Count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(8));
        var position = head.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8 + 2 + (ifd0Count * 12)), (uint)position);
        while (position + 12 <= length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(position + 2), (uint)(position + 6));
            position += 6;
        }

        return block;
    }

    private static T RunTimedOnSmallStack<T>(Func<T> action)
    {
        // Warms up the code paths, so the timing covers the crafted input only.
        MediaMetadataSanitizer.Sanitize(PhotoFixtures.PhoneJpeg(64, 48));

        T result = default!;
        Exception? failure = null;
        var stopwatch = new Stopwatch();
        var thread = new Thread(
            () =>
            {
                try
                {
                    stopwatch.Start();
                    result = action();
                    stopwatch.Stop();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            256 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        Assert.True(stopwatch.Elapsed < Budget, $"Sanitizing took {stopwatch.Elapsed}.");
        return result;
    }

    private static void AssertNoLocation(byte[] sanitized)
    {
        Assert.False(PhotoFixtures.ContainsSecret(sanitized), "A SECRET- value survived.");
        Assert.Empty(Read(sanitized).OfType<GpsDirectory>());
    }

    private static IReadOnlyList<MetadataExtractor.Directory> Read(byte[] bytes) => ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
}
