using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Xmp;
using NetVips;

namespace Cichlids.Infrastructure.Tests;

[Collection(LibvipsCollection.Name)]
public sealed class ImageProcessorTests
{
    public static TheoryData<string, byte[]> UnsupportedFormats => new()
    {
        { "svg", SyntheticImages.Svg() },
        { "avif", SyntheticImages.HeifHeader("avif") },
        { "heic", SyntheticImages.HeifHeader("heic") },
        { "pdf", SyntheticImages.Pdf() },
        { "bmp", SyntheticImages.Bmp(4, 3) },
    };

    [Theory]
    [MemberData(nameof(UnsupportedFormats))]
    public void Rejects_a_format_outside_the_allowlist(string format, byte[] bytes)
    {
        Assert.NotNull(format);
        Assert.Throws<ImageProcessor.UnsupportedImageFormatException>(() => ImageProcessor.Process(bytes));
    }

    [Theory]
    [InlineData(".jpg", ImageFormat.Jpeg)]
    [InlineData(".png", ImageFormat.Png)]
    [InlineData(".webp", ImageFormat.Webp)]
    [InlineData(".gif", ImageFormat.Gif)]
    [InlineData(".tif", ImageFormat.Tiff)]
    public void Processes_a_format_on_the_allowlist(string extension, ImageFormat expectedFormat)
    {
        using var image = (Image.Black(500, 300) + new[] { 10, 20, 30 }).Cast(Enums.BandFormat.Uchar);
        var bytes = image.WriteToBuffer(extension);

        var result = ImageProcessor.Process(bytes);

        Assert.Equal(expectedFormat, result.Format);
        Assert.Equal((500, 300), (result.Width, result.Height));
        Assert.Equal(["thumb", "small"], result.Variants.Select(v => v.Label));
    }

    [Fact]
    public void Throws_a_broken_image_exception_for_bytes_that_start_like_a_jpeg_but_do_not_decode()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, .. "not a real jpeg"u8.ToArray()];

        Assert.Throws<ImageProcessor.BrokenImageException>(() => ImageProcessor.Process(bytes));
    }

    [Fact]
    public void Rejects_a_small_file_declaring_200000_by_200000_pixels_quickly_and_without_decoding_it()
    {
        var bytes = SyntheticImages.PngDeclaring(200_000, 200_000);
        Assert.True(bytes.Length < 16 * 1024, $"The test file has {bytes.Length} bytes.");

        // libvips tracks one process-wide high-water mark without a reset, and earlier tests in
        // this process raise it (thread-pool buffers of a normal decode reach tens of megabytes),
        // so an absolute bound would depend on test order. All libvips test classes share one
        // collection and never run in parallel, so the growth during this call is attributable to
        // it: a header-only rejection allocates a few kilobytes, a decode of the declared size
        // would allocate gigabytes. A spike below the earlier mark stays invisible here; the time
        // limit covers that case, since such a decode takes seconds.
        var highWaterBefore = NetVips.Stats.MemHighwater;
        var stopwatch = Stopwatch.StartNew();
        var ex = Assert.Throws<ImageProcessor.ImageTooLargeException>(() => ImageProcessor.Process(bytes));
        stopwatch.Stop();

        Assert.Contains("100 megapixels", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Rejecting took {stopwatch.Elapsed}.");
        var growth = NetVips.Stats.MemHighwater - highWaterBefore;
        Assert.True(growth < 1024 * 1024, $"Rejecting raised the libvips high-water mark by {growth} bytes.");
    }

    [Theory]
    [InlineData(1, 50_000_000)]
    [InlineData(70_000, 10)]
    [InlineData(30_000, 100)]
    [InlineData(100, 30_000)]
    public void Rejects_a_side_above_65535_pixels_or_a_side_more_than_100_times_the_other_before_decoding(int width, int height)
    {
        var bytes = SyntheticImages.PngDeclaring(width, height, rows: 1);

        var stopwatch = Stopwatch.StartNew();
        var ex = Assert.Throws<ImageProcessor.ImageTooLargeException>(() => ImageProcessor.Process(bytes));
        stopwatch.Stop();

        Assert.Contains($"{width}x{height}", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Rejecting took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Processes_a_strip_whose_long_side_is_exactly_100_times_the_short_side()
    {
        using var image = (Image.Black(100, 10_000) + new[] { 30, 90, 150 }).Cast(Enums.BandFormat.Uchar);

        var result = ImageProcessor.Process(image.WriteToBuffer(".jpg"));

        var thumb = Assert.Single(result.Variants);
        using var decoded = Image.NewFromBuffer(thumb.Bytes);
        Assert.Equal(thumb.Width, decoded.Width);

        // At a width of a few pixels the shrink libvips rounds to can leave the height a few
        // percent above the four-to-one box.
        Assert.InRange(decoded.Height, 1, 4 * 100 * 11 / 10);
    }

    [Fact]
    public void Blocks_libvips_loaders_outside_the_allowlist_for_the_whole_process()
    {
        RuntimeHelpers.RunClassConstructor(typeof(ImageProcessor).TypeHandle);

        Assert.Throws<VipsException>(() => Image.NewFromBuffer(SyntheticImages.Svg()));
    }

    [Fact]
    public void Reports_the_orientation_corrected_size_and_bakes_the_orientation_into_every_variant()
    {
        using var image = Image.Black(1000, 700) + new[] { 120, 40, 200 };
        using var tagged = image.Mutate(m => m.Set(GValue.GIntType, "orientation", 6));
        var bytes = tagged.WriteToBuffer(".jpg");

        var result = ImageProcessor.Process(bytes);

        Assert.Equal((700, 1000), (result.Width, result.Height));
        Assert.Equal([("thumb", 200), ("small", 400)], result.Variants.Select(v => (v.Label, v.Width)));
        foreach (var variant in result.Variants)
        {
            using var decoded = Image.NewFromBuffer(variant.Bytes);
            Assert.Equal(variant.Width, decoded.Width);
            Assert.True(decoded.Height > decoded.Width);
            Assert.Equal(0u, (uint)decoded.GetTypeOf("orientation"));
        }
    }

    [Fact]
    public void Sizes_every_variant_to_its_target_width_without_upscaling()
    {
        using var image = (Image.Black(1700, 2400) + new[] { 30, 90, 150 }).Cast(Enums.BandFormat.Uchar);
        var bytes = image.WriteToBuffer(".jpg");

        var result = ImageProcessor.Process(bytes);

        Assert.Equal(
            [("thumb", 200), ("small", 400), ("medium", 800), ("large", 1600)],
            result.Variants.Select(v => (v.Label, v.Width)));
        foreach (var variant in result.Variants)
        {
            using var decoded = Image.NewFromBuffer(variant.Bytes);
            Assert.Equal(variant.Width, decoded.Width);
        }
    }

    [Fact]
    public void Fits_the_variants_of_a_very_tall_photo_into_four_times_their_width_in_height()
    {
        using var image = (Image.Black(1000, 60_000) + new[] { 30, 90, 150 }).Cast(Enums.BandFormat.Uchar);
        var bytes = image.WriteToBuffer(".jpg", new VOption { { "Q", 50 } });

        var result = ImageProcessor.Process(bytes);

        Assert.Equal((1000, 60_000), (result.Width, result.Height));
        Assert.Equal(
            MediaVariantSpec.Resolve(1000, 60_000).Select(v => (v.Label, v.Width)),
            result.Variants.Select(v => (v.Label, v.Width)));
        foreach (var (variant, targetWidth) in result.Variants.Zip([200, 400, 800]))
        {
            using var decoded = Image.NewFromBuffer(variant.Bytes);
            Assert.Equal(variant.Width, decoded.Width);
            Assert.InRange(decoded.Height, 1, (4 * targetWidth) + 1);
        }
    }

    [Fact]
    public void Converts_a_display_p3_photo_to_srgb_before_removing_its_profile()
    {
        // An in-gamut colour encoded in Display P3 with the profile embedded.
        byte[] p3 = [200, 120, 80];
        using var p3Profile = (Image.Black(1, 1, bands: 3) + 128).Cast(Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb).IccTransform("p3", inputProfile: "srgb");
        var profileBytes = (byte[])p3Profile.Get("icc-profile-data");
        using var flat = (Image.Black(1000, 600) + new[] { (int)p3[0], p3[1], p3[2] }).Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
        using var tagged = flat.Mutate(m => m.Set(GValue.BlobType, "icc-profile-data", profileBytes));
        var bytes = tagged.WriteToBuffer(".jpg", new VOption { { "Q", 100 }, { "subsample_mode", Enums.ForeignSubsample.Off } });

        var result = ImageProcessor.Process(bytes);

        var medium = result.Variants.Single(v => v.Label == "medium");
        using var decoded = Image.NewFromBuffer(medium.Bytes);
        var pixel = decoded.Getpoint(decoded.Width / 2, decoded.Height / 2);
        var expected = ExpectedSrgbFromDisplayP3(p3);
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(pixel[channel], expected[channel] - 3, expected[channel] + 3);
        }
    }

    [Fact]
    public void Writes_variants_without_any_metadata()
    {
        using var p3Profile = (Image.Black(1, 1, bands: 3) + 128).Cast(Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb).IccTransform("p3", inputProfile: "srgb");
        var profileBytes = (byte[])p3Profile.Get("icc-profile-data");
        using var flat = (Image.Black(900, 600) + new[] { 90, 140, 60 }).Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
        using var tagged = flat.Mutate(m =>
        {
            m.Set(GValue.BlobType, "icc-profile-data", profileBytes);
            m.Set(GValue.BlobType, "xmp-data", "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"></x:xmpmeta>"u8.ToArray());
            m.Set(GValue.GIntType, "orientation", 3);
        });
        var bytes = tagged.WriteToBuffer(".jpg");
        Assert.NotEmpty(ImageMetadataReader.ReadMetadata(new MemoryStream(bytes)).OfType<ExifIfd0Directory>());

        var result = ImageProcessor.Process(bytes);

        foreach (var variant in result.Variants)
        {
            using var decoded = Image.NewFromBuffer(variant.Bytes);
            Assert.Equal(0u, (uint)decoded.GetTypeOf("icc-profile-data"));
            Assert.Equal(0u, (uint)decoded.GetTypeOf("exif-data"));
            Assert.Equal(0u, (uint)decoded.GetTypeOf("xmp-data"));

            var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(variant.Bytes));
            Assert.Empty(directories.OfType<ExifDirectoryBase>());
            Assert.Empty(directories.OfType<IccDirectory>());
            Assert.Empty(directories.OfType<XmpDirectory>());
        }
    }

    [Fact]
    public void Converts_an_untagged_cmyk_jpeg_to_rgb_variants()
    {
        using var cmyk = (Image.Black(500, 300, bands: 4) + new[] { 0, 200, 200, 0 }).Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Cmyk);
        var bytes = cmyk.WriteToBuffer(".jpg");

        var result = ImageProcessor.Process(bytes);

        foreach (var variant in result.Variants)
        {
            using var decoded = Image.NewFromBuffer(variant.Bytes);
            Assert.Equal(3, decoded.Bands);
            Assert.Equal(Enums.Interpretation.Srgb, decoded.Interpretation);
        }
    }

    [Fact]
    public void Reads_the_source_stream_once()
    {
        using var image = Image.Black(3000, 2000) + new[] { 120, 40, 200 };
        var bytes = image.WriteToBuffer(".jpg", new VOption { { "Q", 90 } });
        using var counting = new CountingStream(new MemoryStream(bytes));

        var result = ImageProcessor.Process(counting);

        Assert.Equal(4, result.Variants.Count);
        Assert.InRange(counting.BytesRead, bytes.Length, bytes.Length + (64 * 1024));
    }

    // Display P3 and sRGB share the sRGB transfer curve and the D65 white point, so the expected
    // sRGB value is the P3 value linearised, taken through XYZ and encoded again.
    private static double[] ExpectedSrgbFromDisplayP3(byte[] p3)
    {
        double[,] p3ToXyz =
        {
            { 0.4865709, 0.2656677, 0.1982173 },
            { 0.2289746, 0.6917385, 0.0792869 },
            { 0.0000000, 0.0451134, 1.0439444 },
        };
        double[,] xyzToSrgb =
        {
            { 3.2404542, -1.5371385, -0.4985314 },
            { -0.9692660, 1.8760108, 0.0415560 },
            { 0.0556434, -0.2040259, 1.0572252 },
        };

        var linear = p3.Select(v => ToLinear(v / 255.0)).ToArray();
        var xyz = Multiply(p3ToXyz, linear);
        var srgbLinear = Multiply(xyzToSrgb, xyz);
        return srgbLinear.Select(v => Math.Round(FromLinear(Math.Clamp(v, 0, 1)) * 255)).ToArray();

        static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        static double FromLinear(double c) => c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;

        static double[] Multiply(double[,] m, double[] v) =>
            [
                (m[0, 0] * v[0]) + (m[0, 1] * v[1]) + (m[0, 2] * v[2]),
                (m[1, 0] * v[0]) + (m[1, 1] * v[1]) + (m[1, 2] * v[2]),
                (m[2, 0] * v[0]) + (m[2, 1] * v[1]) + (m[2, 2] * v[2]),
            ];
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
