using Cichlids.Infrastructure.Video;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class HlsArgumentsTests
{
    private static IReadOnlyList<string> Build(int width, int height, bool audio, int threads = 2, double? frameRate = 30)
    {
        var probe = new VideoProbe(TimeSpan.FromSeconds(10), width, height, 0, "h264", audio ? "aac" : null, frameRate);
        return HlsArguments.Build("/in/original.mp4", "/out", HlsRenditionLadder.Resolve(probe), audio, threads, frameRate, TimeSpan.FromSeconds(180));
    }

    private static string ValueOf(IReadOnlyList<string> args, string option) => args[args.ToList().IndexOf(option) + 1];

    [Fact]
    public void SplitsTheInputIntoOneScaledOutputPerRendition()
    {
        var args = Build(1920, 1080, audio: true);

        var graph = ValueOf(args, "-filter_complex");
        Assert.StartsWith("[0:v:0]split=3[s0][s1][s2];", graph);
        Assert.Contains("[s0]scale=854:480", graph);
        Assert.Contains("[s2]scale=1920:1080", graph);
        Assert.Equal(3, args.Count(a => a == "[o0]" || a == "[o1]" || a == "[o2]"));
    }

    [Fact]
    public void MapsAudioOncePerRenditionWhenThereIsAudio()
    {
        var args = Build(1920, 1080, audio: true);

        Assert.Equal(3, args.Count(a => a == "0:a:0"));
        Assert.Equal("v:0,a:0,name:480p v:1,a:1,name:720p v:2,a:2,name:1080p", ValueOf(args, "-var_stream_map"));
    }

    [Fact]
    public void LeavesOutAudioOptionsForSilentVideo()
    {
        var args = Build(1280, 720, audio: false);

        Assert.DoesNotContain("0:a:0", args);
        Assert.DoesNotContain("aac", args);
        Assert.Equal("v:0,name:480p v:1,name:720p", ValueOf(args, "-var_stream_map"));
    }

    [Fact]
    public void WritesFragmentedMp4SegmentsAndAMasterPlaylist()
    {
        var args = Build(1280, 720, audio: true);

        Assert.Equal("hls", args[args.ToList().LastIndexOf("-f") + 1]);
        Assert.Equal("fmp4", ValueOf(args, "-hls_segment_type"));
        Assert.Equal("vod", ValueOf(args, "-hls_playlist_type"));
        Assert.Equal("master.m3u8", ValueOf(args, "-master_pl_name"));
        Assert.Equal("/out/%v/seg_%05d.m4s", ValueOf(args, "-hls_segment_filename"));
        Assert.Equal("/out/%v/index.m3u8", args[^1]);
    }

    [Fact]
    public void DropsSourceMetadataAndBoundsTheThreads()
    {
        var args = Build(1280, 720, audio: true, threads: 3);

        Assert.Equal("-1", ValueOf(args, "-map_metadata"));
        Assert.Equal("-1", ValueOf(args, "-map_chapters"));
        Assert.Equal("3", ValueOf(args, "-threads"));
        Assert.Contains("-nostdin", args);
    }

    [Fact]
    public void PutsTheInputPathInOneArgumentWhateverItContains()
    {
        var probe = new VideoProbe(TimeSpan.FromSeconds(10), 1280, 720, 0, "h264", null, 30);
        var args = HlsArguments.Build("/in/we ird $(x);name.mp4", "/out dir", HlsRenditionLadder.Resolve(probe), false, 2, 30, TimeSpan.FromSeconds(180));

        Assert.Equal("/in/we ird $(x);name.mp4", ValueOf(args, "-i"));
    }

    [Fact]
    public void RestrictsTheInputToTheLocalFileAndTheMp4Demuxer()
    {
        var args = Build(1280, 720, audio: true);

        var input = args.ToList().IndexOf("-i");
        Assert.Equal(["-protocol_whitelist", "file", "-f", "mov"], args.Skip(input - 4).Take(4));
    }

    [Fact]
    public void StopsAfterTheLengthLimitWhateverTheContainerHeaderSays()
    {
        var args = Build(1280, 720, audio: true);

        Assert.Equal("180", ValueOf(args, "-t"));
        Assert.True(args.ToList().IndexOf("-t") > args.ToList().IndexOf("-i"));
    }

    [Fact]
    public void ProcessesOnlyTheFirstVideoAndTheFirstAudioStream()
    {
        var args = Build(1920, 1080, audio: true);

        Assert.All(args.Where(a => a.StartsWith("0:", StringComparison.Ordinal)), a => Assert.Equal("0:a:0", a));
        Assert.Contains("-dn", args);
        Assert.Contains("-sn", args);
        Assert.StartsWith("[0:v:0]", ValueOf(args, "-filter_complex"));
    }

    [Theory]
    [InlineData(60.0, true)]
    [InlineData(30.5, true)]
    [InlineData(null, true)]
    [InlineData(30.0, false)]
    [InlineData(24.0, false)]
    public void LimitsTheFrameRateTo30(double? frameRate, bool limited)
    {
        var graph = ValueOf(Build(1280, 720, audio: false, frameRate: frameRate), "-filter_complex");

        Assert.Equal(limited, graph.Contains("fps=30", StringComparison.Ordinal));
    }
}
