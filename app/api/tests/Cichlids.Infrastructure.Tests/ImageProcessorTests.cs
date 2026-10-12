using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using NetVips;

namespace Cichlids.Infrastructure.Tests;

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
        Assert.Throws<ImageProcessor.UnsupportedImageFormatException>(() => ImageProcessor.Measure(bytes));
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".gif")]
    [InlineData(".tif")]
    public void Measures_a_format_on_the_allowlist(string extension)
    {
        using var image = Image.Black(30, 20) + new[] { 10, 20, 30 };
        var bytes = image.WriteToBuffer(extension);

        Assert.Equal((30, 20), ImageProcessor.Measure(bytes));
    }

    [Fact]
    public void Rejects_a_small_file_declaring_200000_by_200000_pixels_quickly_and_without_decoding_it()
    {
        var bytes = SyntheticImages.PngDeclaring(200_000, 200_000);
        Assert.True(bytes.Length < 16 * 1024, $"The test file has {bytes.Length} bytes.");

        var stopwatch = Stopwatch.StartNew();
        var ex = Assert.Throws<ImageProcessor.ImageTooLargeException>(() => ImageProcessor.Measure(bytes));
        stopwatch.Stop();

        Assert.Contains("100 megapixels", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Rejecting took {stopwatch.Elapsed}.");
        Assert.True(NetVips.Stats.MemHighwater < 64L * 1024 * 1024, $"libvips allocated up to {NetVips.Stats.MemHighwater} bytes.");
    }

    [Fact]
    public void Rejects_a_variant_request_for_a_file_declaring_too_many_pixels()
    {
        var bytes = SyntheticImages.PngDeclaring(200_000, 200_000);

        Assert.Throws<ImageProcessor.ImageTooLargeException>(() => ImageProcessor.GenerateVariant(bytes, 200));
    }

    [Fact]
    public void Blocks_libvips_loaders_outside_the_allowlist_for_the_whole_process()
    {
        RuntimeHelpers.RunClassConstructor(typeof(ImageProcessor).TypeHandle);

        Assert.Throws<VipsException>(() => Image.NewFromBuffer(SyntheticImages.Svg()));
    }
}
