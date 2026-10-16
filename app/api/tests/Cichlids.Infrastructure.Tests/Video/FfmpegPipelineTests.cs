using System.Text.Json;
using Cichlids.Infrastructure.Video;
using Cichlids.Tests.Shared;

namespace Cichlids.Infrastructure.Tests.Video;

[Collection(LibvipsCollection.Name)]
public sealed class FfmpegPipelineTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ffmpeg-pipeline-").FullName;
    private readonly VideoToolOptions _options = new() { TranscodeTimeout = TimeSpan.FromMinutes(2) };
    private readonly ProcessRunner _runner = new();
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(180);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<string> WriteVideoAsync(
        int width = 320, int height = 180, double seconds = 2, bool audio = true, int rotation = 0, string codec = "libx264")
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, await TestVideos.CreateAsync(width, height, seconds, audio, rotation, codec));
        return path;
    }

    private HlsTranscoder Transcoder() => new(_runner, new PosterFrameSelector(_runner, _options), _options);

    private async Task<JsonElement> ProbeJsonAsync(string path)
    {
        var json = await TestVideos.RunAsync("ffprobe", ["-v", "error", "-print_format", "json", "-show_streams", "-i", path]);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task ProbeReadsDurationSizeAndCodecs()
    {
        var path = await WriteVideoAsync(seconds: 2);

        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);

        Assert.InRange(probe.Duration.TotalSeconds, 1.8, 2.3);
        Assert.Equal((320, 180), (probe.Width, probe.Height));
        Assert.Equal(("h264", "aac"), (probe.VideoCodec, probe.AudioCodec));
        Assert.Equal(0, probe.RotationDegrees);
    }

    [Fact]
    public async Task ProbeReadsTheRotationOfAPhoneVideo()
    {
        var path = await WriteVideoAsync(width: 320, height: 180, rotation: 90);

        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);

        Assert.Equal(90, probe.RotationDegrees);
        Assert.Equal((180, 320), (probe.DisplayWidth, probe.DisplayHeight));
    }

    [Fact]
    public async Task ProbeRefusesBytesThatAreNoVideo()
    {
        var path = Path.Combine(_directory, "broken.mp4");
        await File.WriteAllBytesAsync(path, "this is definitely not a video file"u8.ToArray());

        var error = await Assert.ThrowsAsync<VideoProbeException>(
            () => new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
    }

    [Fact]
    public async Task ProbeRefusesATruncatedVideo()
    {
        var bytes = await TestVideos.CreateAsync(seconds: 2);
        var path = Path.Combine(_directory, "truncated.mp4");
        await File.WriteAllBytesAsync(path, bytes.AsMemory(0, 40).ToArray());

        await Assert.ThrowsAsync<VideoProbeException>(
            () => new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ProbeRefusesAVideoWithAnUnsupportedCodec()
    {
        var path = await WriteVideoAsync(codec: "mpeg4", audio: false);

        var error = await Assert.ThrowsAsync<VideoProbeException>(
            () => new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None));

        Assert.Equal(VideoProbeException.UnsupportedCodec, error.Code);
    }

    [Fact]
    public async Task TranscodesToHlsRenditionsNeverAboveTheSourceWithAPosterFrame()
    {
        var path = await WriteVideoAsync(width: 1280, height: 720, seconds: 2);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var output = Path.Combine(_directory, "out");

        var result = await Transcoder().TranscodeAsync(path, probe, output, Limit, CancellationToken.None);

        Assert.Equal(["480p", "720p"], result.Renditions.Select(r => r.Label));
        var master = await File.ReadAllTextAsync(Path.Combine(output, "master.m3u8"));
        Assert.Contains("480p/index.m3u8", master);
        Assert.Contains("720p/index.m3u8", master);
        Assert.Contains("RESOLUTION=1280x720", master);
        Assert.DoesNotContain("1080", master);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "720p"), "init*.mp4"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "720p"), "seg_*.m4s"));

        var rendition = await ProbeJsonAsync(Path.Combine(output, "480p", "index.m3u8"));
        var streams = rendition.GetProperty("streams").EnumerateArray().ToList();
        Assert.Equal(480, streams.Single(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("height").GetInt32());
        Assert.Contains(streams, s => s.GetProperty("codec_type").GetString() == "audio");

        Assert.Contains("poster.jpg", result.Files);
        var poster = Path.Combine(output, "poster.jpg");
        Assert.True((await File.ReadAllBytesAsync(poster)).AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]));
        Assert.All(result.Files, file => Assert.True(File.Exists(Path.Combine(output, file))));
    }

    [Fact]
    public async Task TranscodesAnUprightPortraitVideoFromARotatedSource()
    {
        var path = await WriteVideoAsync(width: 1280, height: 720, seconds: 2, audio: false, rotation: 90);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var output = Path.Combine(_directory, "portrait");

        var result = await Transcoder().TranscodeAsync(path, probe, output, Limit, CancellationToken.None);

        Assert.Equal([(480, 854), (720, 1280)], result.Renditions.Select(r => (r.Width, r.Height)));
        var video = (await ProbeJsonAsync(Path.Combine(output, "720p", "index.m3u8")))
            .GetProperty("streams").EnumerateArray().Single(s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal((720, 1280), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32()));
        Assert.DoesNotContain(
            (await ProbeJsonAsync(Path.Combine(output, "480p", "index.m3u8"))).GetProperty("streams").EnumerateArray(),
            s => s.GetProperty("codec_type").GetString() == "audio");
    }

    [Fact]
    public async Task KeepsAVideoBelow480pAtItsOwnSize()
    {
        var path = await WriteVideoAsync(width: 320, height: 180, seconds: 1);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var output = Path.Combine(_directory, "small");

        var result = await Transcoder().TranscodeAsync(path, probe, output, Limit, CancellationToken.None);

        var rendition = Assert.Single(result.Renditions);
        Assert.Equal((320, 180), (rendition.Width, rendition.Height));
        Assert.True(File.Exists(Path.Combine(output, "180p", "index.m3u8")));
    }

    [Fact]
    public async Task StopsFfmpegWhenTheTranscodeTimeoutPasses()
    {
        var path = await WriteVideoAsync(width: 1280, height: 720, seconds: 2);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var options = new VideoToolOptions { TranscodeTimeout = TimeSpan.FromMilliseconds(1) };
        var transcoder = new HlsTranscoder(_runner, new PosterFrameSelector(_runner, options), options);

        await Assert.ThrowsAsync<ProcessTimeoutException>(
            () => transcoder.TranscodeAsync(path, probe, Path.Combine(_directory, "timeout"), Limit, CancellationToken.None));
    }

    [Fact]
    public async Task ReportsFfmpegFailuresAsATranscodeError()
    {
        var path = await WriteVideoAsync(seconds: 1);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var options = new VideoToolOptions { FfmpegPath = "false" };
        var transcoder = new HlsTranscoder(_runner, new PosterFrameSelector(_runner, options), options);

        await Assert.ThrowsAsync<VideoTranscodeException>(
            () => transcoder.TranscodeAsync(path, probe, Path.Combine(_directory, "failing"), Limit, CancellationToken.None));
    }

    [Fact]
    public async Task RefusesAVideoWhoseDecodedLengthExceedsTheLimitEvenWhenTheHeaderClaimsLess()
    {
        var path = await WriteVideoAsync(width: 640, height: 360, seconds: 6);
        var lyingProbe = new VideoProbe(TimeSpan.FromSeconds(1), 640, 360, 0, "h264", "aac", 25);

        var error = await Assert.ThrowsAsync<VideoProbeException>(() => Transcoder()
            .TranscodeAsync(path, lyingProbe, Path.Combine(_directory, "long"), TimeSpan.FromSeconds(3), CancellationToken.None));

        Assert.Equal(VideoProbeException.TooLong, error.Code);
    }

    [Fact]
    public async Task AcceptsAVideoWithinTheLimit()
    {
        var path = await WriteVideoAsync(width: 640, height: 360, seconds: 2);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);

        var result = await Transcoder().TranscodeAsync(path, probe, Path.Combine(_directory, "ok"), TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.NotEmpty(result.Renditions);
    }

    [Fact]
    public async Task LimitsTheOutputFrameRateTo30()
    {
        var path = Path.Combine(_directory, "fast.mp4");
        await TestVideos.RunAsync("ffmpeg", ["-nostdin", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=60:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", path]);
        var probe = await new FfprobeClient(_runner, _options).ProbeAsync(path, CancellationToken.None);
        var output = Path.Combine(_directory, "fps");

        await Transcoder().TranscodeAsync(path, probe, output, Limit, CancellationToken.None);

        var video = (await ProbeJsonAsync(Path.Combine(output, "360p", "index.m3u8")))
            .GetProperty("streams").EnumerateArray().Single(s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("30/1", video.GetProperty("avg_frame_rate").GetString());
    }

    [Fact]
    public async Task AsksFfprobeOnlyForTheFieldsItNeedsAndBoundsItsOutput()
    {
        var recorder = new CapturingRunner(_runner);
        var path = await WriteVideoAsync(seconds: 1);

        await new FfprobeClient(recorder, _options).ProbeAsync(path, CancellationToken.None);

        var request = Assert.Single(recorder.Requests);
        Assert.Contains("-show_entries", request.Arguments);
        Assert.DoesNotContain("-show_streams", request.Arguments);
        Assert.NotNull(request.MaxStandardOutput);
        Assert.True(request.MaxStandardOutput <= FfprobeJsonParser.MaxJsonCharacters);
    }

    private sealed class CapturingRunner(IProcessRunner inner) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return inner.RunAsync(request, cancellationToken);
        }
    }
}
