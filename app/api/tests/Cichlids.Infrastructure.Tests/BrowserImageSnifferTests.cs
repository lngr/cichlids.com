using System.Buffers.Binary;
using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using NetVips;

namespace Cichlids.Infrastructure.Tests;

[Collection(LibvipsCollection.Name)]
public sealed class BrowserImageSnifferTests
{
    [Fact]
    public void Reads_the_type_and_size_of_a_bmp()
    {
        Assert.Equal(new BrowserImage("image/bmp", 7, 5), BrowserImageSniffer.Sniff(SyntheticImages.Bmp(7, 5)));
    }

    [Fact]
    public void Reads_the_size_of_a_top_down_bmp()
    {
        var bmp = SyntheticImages.Bmp(7, 5);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -5);

        Assert.Equal(new BrowserImage("image/bmp", 7, 5), BrowserImageSniffer.Sniff(bmp));
    }

    [Fact]
    public void Reads_the_size_of_a_bmp_with_a_core_header()
    {
        var bmp = new byte[26 + 12];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(18), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(20), 4);

        Assert.Equal(new BrowserImage("image/bmp", 9, 4), BrowserImageSniffer.Sniff(bmp));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".webp", "image/webp")]
    public void Reads_the_type_and_size_of_a_libvips_encoded_image(string suffix, string contentType)
    {
        using var image = PhotoFixtures.Flat(37, 23);

        Assert.Equal(new BrowserImage(contentType, 37, 23), BrowserImageSniffer.Sniff(image.WriteToBuffer(suffix)));
    }

    [Fact]
    public void Reads_the_size_of_a_lossless_webp()
    {
        using var image = PhotoFixtures.Flat(37, 23);
        var webp = image.WriteToBuffer(".webp", new VOption { { "lossless", true } });

        Assert.Equal(new BrowserImage("image/webp", 37, 23), BrowserImageSniffer.Sniff(webp));
    }

    [Fact]
    public void Reads_the_size_of_an_extended_webp()
    {
        using var image = PhotoFixtures.WithIcc(PhotoFixtures.Flat(37, 23));
        var webp = image.WriteToBuffer(".webp", new VOption { { "keep", Enums.ForeignKeep.Icc } });
        Assert.Equal("VP8X", System.Text.Encoding.ASCII.GetString(webp, 12, 4));

        Assert.Equal(new BrowserImage("image/webp", 37, 23), BrowserImageSniffer.Sniff(webp));
    }

    [Fact]
    public void Reads_the_frame_size_of_a_jpeg_behind_its_metadata_segments()
    {
        Assert.Equal(new BrowserImage("image/jpeg", 64, 48), BrowserImageSniffer.Sniff(PhotoFixtures.PhoneJpeg(64, 48)));
    }

    [Fact]
    public void Returns_null_for_bytes_no_browser_renders()
    {
        Assert.Null(BrowserImageSniffer.Sniff([]));
        Assert.Null(BrowserImageSniffer.Sniff("not an image at all"u8.ToArray()));
        Assert.Null(BrowserImageSniffer.Sniff(SyntheticImages.Pdf()));
        Assert.Null(BrowserImageSniffer.Sniff(SyntheticImages.Svg()));
        Assert.Null(BrowserImageSniffer.Sniff(PhotoFixtures.UndecodableTiffWithGps()));
    }

    [Fact]
    public void Returns_null_for_a_header_cut_before_its_size()
    {
        Assert.Null(BrowserImageSniffer.Sniff(SyntheticImages.Bmp(7, 5)[..20]));
        Assert.Null(BrowserImageSniffer.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]));
        Assert.Null(BrowserImageSniffer.Sniff("GIF89a\x01"u8.ToArray()));
    }

    [Fact]
    public void Returns_null_for_a_bmp_without_a_positive_size()
    {
        Assert.Null(BrowserImageSniffer.Sniff(SyntheticImages.Bmp(0, 5)));
        Assert.Null(BrowserImageSniffer.Sniff(SyntheticImages.Bmp(5, 0)));
    }
}
