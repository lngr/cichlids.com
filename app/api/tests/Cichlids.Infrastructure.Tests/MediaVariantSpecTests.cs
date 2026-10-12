using Cichlids.Infrastructure.Media;

namespace Cichlids.Infrastructure.Tests;

public sealed class MediaVariantSpecTests
{
    [Theory]
    [InlineData(150, new[] { "thumb" }, new[] { 150 })]
    [InlineData(199, new[] { "thumb" }, new[] { 199 })]
    [InlineData(200, new[] { "thumb" }, new[] { 200 })]
    [InlineData(201, new[] { "thumb" }, new[] { 200 })]
    [InlineData(400, new[] { "thumb" }, new[] { 200 })]
    [InlineData(800, new[] { "thumb", "small" }, new[] { 200, 400 })]
    [InlineData(899, new[] { "thumb", "small", "medium" }, new[] { 200, 400, 800 })]
    [InlineData(900, new[] { "thumb", "small", "medium" }, new[] { 200, 400, 800 })]
    [InlineData(1600, new[] { "thumb", "small", "medium" }, new[] { 200, 400, 800 })]
    [InlineData(1601, new[] { "thumb", "small", "medium", "large" }, new[] { 200, 400, 800, 1600 })]
    [InlineData(2400, new[] { "thumb", "small", "medium", "large" }, new[] { 200, 400, 800, 1600 })]
    public void ResolvesTheExpectedLabelsAndWidths(int originalWidth, string[] expectedLabels, int[] expectedWidths)
    {
        var resolved = MediaVariantSpec.Resolve(originalWidth, originalWidth);

        Assert.Equal(expectedLabels, resolved.Select(v => v.Label));
        Assert.Equal(expectedWidths, resolved.Select(v => v.Width));
    }

    [Theory]
    [InlineData(1000, 100_000, new[] { "thumb", "small", "medium" }, new[] { 8, 16, 32 })]
    [InlineData(1700, 58_000, new[] { "thumb", "small", "medium", "large" }, new[] { 23, 46, 93, 187 })]
    [InlineData(1000, 4_000, new[] { "thumb", "small", "medium" }, new[] { 200, 400, 800 })]
    [InlineData(150, 1_200, new[] { "thumb" }, new[] { 75 })]
    public void FitsAVeryTallPhotoIntoFourTimesTheTargetWidthInHeight(int width, int height, string[] expectedLabels, int[] expectedWidths)
    {
        var resolved = MediaVariantSpec.Resolve(width, height);

        Assert.Equal(expectedLabels, resolved.Select(v => v.Label));
        Assert.Equal(expectedWidths, resolved.Select(v => v.Width));
    }
}
