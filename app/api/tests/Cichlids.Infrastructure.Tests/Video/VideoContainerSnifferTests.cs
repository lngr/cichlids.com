using System.Text;
using Cichlids.Infrastructure.Video;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class VideoContainerSnifferTests
{
    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var size = 16 + (4 * compatible.Length);
        var bytes = new List<byte> { 0, 0, (byte)(size >> 8), (byte)size };
        bytes.AddRange(Encoding.ASCII.GetBytes("ftyp"));
        bytes.AddRange(Encoding.ASCII.GetBytes(major));
        bytes.AddRange(new byte[] { 0, 0, 2, 0 });
        foreach (var brand in compatible)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(brand));
        }

        bytes.AddRange(new byte[64]);
        return [.. bytes];
    }

    [Theory]
    [InlineData("isom")]
    [InlineData("mp42")]
    [InlineData("mp41")]
    [InlineData("avc1")]
    [InlineData("iso5")]
    [InlineData("M4V ")]
    public void RecognizesMp4Brands(string brand) =>
        Assert.Equal(VideoContainer.Mp4, VideoContainerSniffer.Sniff(Ftyp(brand)));

    [Fact]
    public void RecognizesQuickTime() =>
        Assert.Equal(VideoContainer.QuickTime, VideoContainerSniffer.Sniff(Ftyp("qt  ", "qt  ")));

    [Fact]
    public void RecognizesAnMp4ByItsCompatibleBrands() =>
        Assert.Equal(VideoContainer.Mp4, VideoContainerSniffer.Sniff(Ftyp("XXXX", "isom", "mp42")));

    [Theory]
    [InlineData("heic")]
    [InlineData("avif")]
    [InlineData("M4A ")]
    public void RefusesFamiliesThatAreNotVideo(string brand) =>
        Assert.Null(VideoContainerSniffer.Sniff(Ftyp(brand, "mif1")));

    [Fact]
    public void RefusesBytesWithoutAFileTypeBox()
    {
        Assert.Null(VideoContainerSniffer.Sniff("GIF89a not a video at all......."u8));
        Assert.Null(VideoContainerSniffer.Sniff([]));
        Assert.Null(VideoContainerSniffer.Sniff([0, 0, 0, 8, (byte)'f', (byte)'t']));
    }
}
