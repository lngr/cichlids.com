using System.Diagnostics;
using Cichlids.Infrastructure.Video;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task CapturesOutputAndTheExitCode()
    {
        var result = await _runner.RunAsync(
            new ProcessRequest("sh", ["-c", "echo out; echo err >&2; exit 3"], TimeSpan.FromSeconds(10)), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StandardOutput.Trim());
        Assert.Equal("err", result.StandardError.Trim());
    }

    [Fact]
    public async Task PassesArgumentsWithoutShellInterpolation()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"process-runner-{Guid.NewGuid():N}");
        try
        {
            var result = await _runner.RunAsync(
                new ProcessRequest("echo", [$"$(touch {marker})", "a;b", "`id`", "'quoted'"], TimeSpan.FromSeconds(10)),
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"$(touch {marker}) a;b `id` 'quoted'", result.StandardOutput.Trim());
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task KillsTheProcessWhenTheTimeoutPasses()
    {
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ProcessTimeoutException>(() => _runner.RunAsync(
            new ProcessRequest("sleep", ["30"], TimeSpan.FromMilliseconds(300)), CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopsTheProcessWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(
            new ProcessRequest("sleep", ["30"], TimeSpan.FromSeconds(60)), cancellation.Token));
    }

    [Fact]
    public async Task RunsAtTheRequestedNiceness()
    {
        // A child inherits the niceness of the test host, and nice adds the requested value to it
        // up to the maximum of 19, so the expectation derives from a child started without nice.
        var baseline = await _runner.RunAsync(
            new ProcessRequest("sh", ["-c", "ps -o ni= -p $$"], TimeSpan.FromSeconds(10)), CancellationToken.None);
        var result = await _runner.RunAsync(
            new ProcessRequest("sh", ["-c", "ps -o ni= -p $$"], TimeSpan.FromSeconds(10), Nice: 10), CancellationToken.None);

        var inherited = int.Parse(baseline.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Math.Min(19, inherited + 10).ToString(System.Globalization.CultureInfo.InvariantCulture), result.StandardOutput.Trim());
    }

    [Fact]
    public async Task KeepsOnlyTheTailOfLongErrorOutput()
    {
        var result = await _runner.RunAsync(
            new ProcessRequest("sh", ["-c", "head -c 200000 /dev/zero | tr '\\0' 'x' >&2; echo END >&2"], TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.True(result.StandardError.Length <= ProcessRunner.MaxCapturedCharacters);
        Assert.EndsWith("END", result.StandardError.Trim());
    }

    [Fact]
    public async Task StopsAProcessWhoseStandardOutputExceedsTheLimit()
    {
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ProcessOutputTooLargeException>(() => _runner.RunAsync(
            new ProcessRequest("yes", [], TimeSpan.FromSeconds(60), MaxStandardOutput: 4096), CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReturnsStandardOutputWithinTheLimit()
    {
        var result = await _runner.RunAsync(
            new ProcessRequest("echo", ["hello"], TimeSpan.FromSeconds(10), MaxStandardOutput: 4096), CancellationToken.None);

        Assert.Equal("hello", result.StandardOutput.Trim());
    }
}
