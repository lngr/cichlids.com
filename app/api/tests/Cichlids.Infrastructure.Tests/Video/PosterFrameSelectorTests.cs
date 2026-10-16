using Cichlids.Infrastructure.Video;
using NetVips;

namespace Cichlids.Infrastructure.Tests.Video;

[Collection(LibvipsCollection.Name)]
public sealed class PosterFrameSelectorTests
{
    private static string WriteImage(string directory, string name, Func<Image> create)
    {
        var path = Path.Combine(directory, name);
        using var image = create();
        image.WriteToFile(path);
        return path;
    }

    [Fact]
    public void ScoresASharpFrameAboveABlurredOneAndAFlatFrameLowest()
    {
        var directory = Directory.CreateTempSubdirectory("poster-score-").FullName;
        try
        {
            var sharp = WriteImage(directory, "sharp.png", () => Image.Gaussnoise(640, 360, mean: 128, sigma: 40));
            var blurred = WriteImage(directory, "blurred.png", () => Image.Gaussnoise(640, 360, mean: 128, sigma: 40).Gaussblur(4));
            var flat = WriteImage(directory, "flat.png", () => Image.Black(640, 360) + 128);

            var scores = new[] { sharp, blurred, flat }.Select(PosterFrameSelector.Sharpness).ToArray();

            Assert.True(scores[0] > scores[1], $"sharp {scores[0]} should exceed blurred {scores[1]}");
            Assert.True(scores[1] > scores[2], $"blurred {scores[1]} should exceed flat {scores[2]}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 3)]
    [InlineData(10, 5)]
    [InlineData(180, 5)]
    public void SamplesMoreCandidatesFromLongerVideo(double seconds, int expectedCount)
    {
        var times = PosterFrameSelector.CandidateTimes(TimeSpan.FromSeconds(seconds));

        Assert.Equal(expectedCount, times.Count);
        Assert.All(times, t => Assert.InRange(t.TotalSeconds, 0, seconds));
        Assert.Equal(times.OrderBy(t => t), times);
    }
}
