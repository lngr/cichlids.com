using Cichlids.Infrastructure.Video;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class HlsRenditionLadderTests
{
    private static string[] Labels(int width, int height) =>
        HlsRenditionLadder.Resolve(width, height).Select(r => r.Label).ToArray();

    [Theory]
    [InlineData(3840, 2160, new[] { "480p", "720p", "1080p" })]
    [InlineData(1920, 1080, new[] { "480p", "720p", "1080p" })]
    [InlineData(1280, 720, new[] { "480p", "720p" })]
    [InlineData(1600, 900, new[] { "480p", "720p" })]
    [InlineData(854, 480, new[] { "480p" })]
    [InlineData(640, 360, new[] { "360p" })]
    [InlineData(320, 240, new[] { "240p" })]
    public void NeverOffersARenditionAboveTheSource(int width, int height, string[] expected) =>
        Assert.Equal(expected, Labels(width, height));

    [Theory]
    [InlineData(1080, 1920, new[] { "480p", "720p", "1080p" })]
    [InlineData(720, 1280, new[] { "480p", "720p" })]
    [InlineData(2160, 3840, new[] { "480p", "720p", "1080p" })]
    public void MeasuresPortraitVideoByItsShortSide(int width, int height, string[] expected) =>
        Assert.Equal(expected, Labels(width, height));

    [Fact]
    public void KeepsTheAspectRatioWithEvenSizes()
    {
        var landscape = HlsRenditionLadder.Resolve(1920, 1080);
        Assert.Equal([(854, 480), (1280, 720), (1920, 1080)], landscape.Select(r => (r.Width, r.Height)));

        var portrait = HlsRenditionLadder.Resolve(1080, 1920);
        Assert.Equal([(480, 854), (720, 1280), (1080, 1920)], portrait.Select(r => (r.Width, r.Height)));

        var odd = HlsRenditionLadder.Resolve(1000, 750);
        Assert.All(odd, r => Assert.True(r.Width % 2 == 0 && r.Height % 2 == 0));
    }

    [Fact]
    public void ShrinksOnlyAndKeepsTheSourceSizeBelowTheLowestRung()
    {
        var rendition = Assert.Single(HlsRenditionLadder.Resolve(321, 241));

        Assert.Equal((320, 240), (rendition.Width, rendition.Height));
    }

    [Fact]
    public void OrdersRenditionsFromLowToHighBitrate()
    {
        var ladder = HlsRenditionLadder.Resolve(1920, 1080);

        Assert.Equal(ladder.OrderBy(r => r.VideoBitrateKbps), ladder);
    }

    [Fact]
    public void ResolvesTheDisplaySizeOfARotatedProbe()
    {
        var probe = new VideoProbe(TimeSpan.FromSeconds(5), 1920, 1080, 90, "h264", null, 30);

        Assert.Equal(["480p", "720p", "1080p"], HlsRenditionLadder.Resolve(probe).Select(r => r.Label));
        Assert.Equal((480, 854), (HlsRenditionLadder.Resolve(probe)[0].Width, HlsRenditionLadder.Resolve(probe)[0].Height));
    }

    [Theory]
    [InlineData(8192, 2048)]
    [InlineData(8192, 4096)]
    [InlineData(7680, 4320)]
    [InlineData(6000, 1500)]
    [InlineData(4000, 1000)]
    [InlineData(2048, 8192)]
    [InlineData(1500, 6000)]
    [InlineData(16, 16)]
    [InlineData(101, 2999)]
    public void BoundsEveryRenditionInShortAndLongSide(int width, int height)
    {
        foreach (var rendition in HlsRenditionLadder.Resolve(width, height))
        {
            Assert.InRange(Math.Min(rendition.Width, rendition.Height), 2, 1080);
            Assert.InRange(Math.Max(rendition.Width, rendition.Height), 2, HlsRenditionLadder.MaxLongSide);
            Assert.True(rendition.Width % 2 == 0 && rendition.Height % 2 == 0);
            Assert.True(rendition.Width <= width && rendition.Height <= height);
        }
    }

    [Fact]
    public void FitsAnExtremeAspectRatioIntoTheLongSideLimitKeepingTheRatio()
    {
        var top = HlsRenditionLadder.Resolve(8192, 2048).Last();

        Assert.Equal(HlsRenditionLadder.MaxLongSide, top.Width);
        Assert.Equal(960, top.Height);
    }
}
