using System.Text;
using Cichlids.Infrastructure.Video;
using Cichlids.Tests.Shared;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class FfmpegInputRestrictionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ffmpeg-input-").FullName;
    private readonly RecordingRunner _runner = new();
    private readonly VideoToolOptions _options = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(1, string.Empty, "refused"));
        }
    }

    private async Task<string> WriteAsync(string name, string content)
    {
        var secret = Path.Combine(_directory, "secret.txt");
        await File.WriteAllTextAsync(secret, "top secret");
        var path = Path.Combine(_directory, name);
        await File.WriteAllTextAsync(path, content.Replace("{secret}", secret, StringComparison.Ordinal), Encoding.ASCII);
        return path;
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:1,\nfile://{secret}\n#EXT-X-ENDLIST\n")]
    [InlineData("#EXTM3U\n#EXTINF:1,\nhttp://127.0.0.1:1/internal.ts\n#EXT-X-ENDLIST\n")]
    [InlineData("ffconcat version 1.0\nfile {secret}\n")]
    [InlineData("<MPD><BaseURL>file://{secret}</BaseURL></MPD>")]
    [InlineData("data:video/mp4;base64,AAAA")]
    public async Task RefusesReferenceFilesBeforeAnyToolRuns(string content)
    {
        var path = await WriteAsync("upload.mp4", content);

        var error = await Assert.ThrowsAsync<VideoProbeException>(
            () => new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
        Assert.Empty(_runner.Requests);
    }

    [Fact]
    public async Task RefusesReferenceFilesBeforeTranscodingOrExtractingAPoster()
    {
        var path = await WriteAsync("upload.mp4", "#EXTM3U\n#EXTINF:1,\nfile://{secret}\n");
        var probe = new VideoProbe(TimeSpan.FromSeconds(2), 1280, 720, 0, "h264", null, 30);
        var transcoder = new HlsTranscoder(_runner, new PosterFrameSelector(_runner, _options), _options);

        await Assert.ThrowsAsync<VideoProbeException>(
            () => transcoder.TranscodeAsync(path, probe, Path.Combine(_directory, "out"), TimeSpan.FromSeconds(180), CancellationToken.None));
        await Assert.ThrowsAsync<VideoProbeException>(() => new PosterFrameSelector(_runner, _options)
            .ExtractAsync(path, Path.Combine(_directory, "p.jpg"), probe.Duration, _directory, CancellationToken.None));

        Assert.Empty(_runner.Requests);
    }

    [Fact]
    public async Task PassesTheWhitelistAndTheDemuxerToFfprobeFfmpegAndThePosterExtraction()
    {
        var path = Path.Combine(_directory, "real.mp4");
        await File.WriteAllBytesAsync(path, await TestVideos.CreateAsync(seconds: 1));
        var probe = new VideoProbe(TimeSpan.FromSeconds(1), 320, 180, 0, "h264", "aac", 25);

        await Assert.ThrowsAsync<VideoProbeException>(() => new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None));
        await Assert.ThrowsAsync<VideoTranscodeException>(() => new HlsTranscoder(_runner, new PosterFrameSelector(_runner, _options), _options)
            .TranscodeAsync(path, probe, Path.Combine(_directory, "o"), TimeSpan.FromSeconds(180), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PosterFrameSelector(_runner, _options)
            .ExtractAsync(path, Path.Combine(_directory, "p.jpg"), probe.Duration, _directory, CancellationToken.None));

        Assert.True(_runner.Requests.Count >= 3);
        foreach (var request in _runner.Requests)
        {
            var args = request.Arguments.ToList();
            var input = args.IndexOf("-i");
            Assert.Equal(["-protocol_whitelist", "file", "-f", "mov"], args.Skip(input - 4).Take(4));
        }
    }

    [Fact]
    public async Task ReadsNoOtherFileThroughAMp4ThatPassesTheSniffing()
    {
        var secret = Path.Combine(_directory, "secret.txt");
        await File.WriteAllTextAsync(secret, "top secret");
        var bytes = await TestVideos.CreateAsync(seconds: 1);
        var path = Path.Combine(_directory, "valid.mp4");
        await File.WriteAllBytesAsync(path, bytes);
        var runner = new ProcessRunner();

        var probe = await new FfprobeClient(runner, _options).ProbeAsync(path, CancellationToken.None);

        Assert.Equal(320, probe.Width);
    }
}
