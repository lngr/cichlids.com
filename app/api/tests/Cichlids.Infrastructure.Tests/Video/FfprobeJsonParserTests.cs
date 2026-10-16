using Cichlids.Infrastructure.Video;

namespace Cichlids.Infrastructure.Tests.Video;

public sealed class FfprobeJsonParserTests
{
    private static string Json(
        string videoExtras = "", string codec = "h264", string duration = "12.5", bool audio = true, int width = 1920, int height = 1080) => $$"""
        {
          "streams": [
            { "index": 0, "codec_type": "video", "codec_name": "{{codec}}", "width": {{width}}, "height": {{height}},
              "avg_frame_rate": "30000/1001" {{videoExtras}} },
            {{(audio ? """{ "index": 1, "codec_type": "audio", "codec_name": "aac" }""" : """{ "index": 1, "codec_type": "data" }""")}}
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "{{duration}}" }
        }
        """;

    [Fact]
    public void ReadsDurationSizeCodecsAndFrameRate()
    {
        var probe = FfprobeJsonParser.Parse(Json());

        Assert.Equal(TimeSpan.FromSeconds(12.5), probe.Duration);
        Assert.Equal((1920, 1080), (probe.Width, probe.Height));
        Assert.Equal("h264", probe.VideoCodec);
        Assert.Equal("aac", probe.AudioCodec);
        Assert.Equal(29.97, probe.FrameRate!.Value, 2);
        Assert.Equal((1920, 1080), (probe.DisplayWidth, probe.DisplayHeight));
    }

    [Fact]
    public void ReportsNoAudioCodecWithoutAnAudioStream() =>
        Assert.Null(FfprobeJsonParser.Parse(Json(audio: false)).AudioCodec);

    [Theory]
    [InlineData(", \"side_data_list\": [ { \"side_data_type\": \"Display Matrix\", \"rotation\": -90 } ]", 270)]
    [InlineData(", \"side_data_list\": [ { \"side_data_type\": \"Display Matrix\", \"rotation\": 90 } ]", 90)]
    [InlineData(", \"side_data_list\": [ { \"side_data_type\": \"Display Matrix\", \"rotation\": 180 } ]", 180)]
    [InlineData(", \"tags\": { \"rotate\": \"90\" }", 90)]
    [InlineData("", 0)]
    public void ReadsTheRotationFromSideDataOrTags(string extras, int expected) =>
        Assert.Equal(expected, FfprobeJsonParser.Parse(Json(extras)).RotationDegrees);

    [Fact]
    public void SwapsTheDisplaySizeForQuarterTurns()
    {
        var probe = FfprobeJsonParser.Parse(Json(", \"tags\": { \"rotate\": \"90\" }"));

        Assert.Equal((1080, 1920), (probe.DisplayWidth, probe.DisplayHeight));
    }

    [Theory]
    [InlineData("vp9")]
    [InlineData("prores")]
    [InlineData("mpeg4")]
    public void RefusesCodecsOutsideH264AndHevc(string codec)
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Json(codec: codec)));

        Assert.Equal(VideoProbeException.UnsupportedCodec, error.Code);
    }

    [Fact]
    public void AcceptsHevc() =>
        Assert.Equal("hevc", FfprobeJsonParser.Parse(Json(codec: "hevc")).VideoCodec);

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"streams\":[],\"format\":{}}")]
    public void RefusesUnreadableOutput(string json)
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(json));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("NaN")]
    public void RefusesAMissingOrNonPositiveDuration(string duration)
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Json(duration: duration)));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
    }

    [Fact]
    public void RefusesAFileWithoutAVideoStream()
    {
        const string json = """{"streams":[{"codec_type":"audio","codec_name":"aac"}],"format":{"duration":"3"}}""";

        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(json));

        Assert.Equal(VideoProbeException.NoVideo, error.Code);
    }

    [Theory]
    [InlineData(100000, 100)]
    [InlineData(8193, 4000)]
    [InlineData(4000, 8193)]
    [InlineData(15, 15)]
    [InlineData(1920, 400)]
    [InlineData(400, 1920)]
    public void RefusesExtremeDimensions(int width, int height)
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Json(width: width, height: height)));

        Assert.Equal(VideoProbeException.UnsupportedDimensions, error.Code);
    }

    [Theory]
    [InlineData(8192, 4320)]
    [InlineData(1920, 480)]
    [InlineData(16, 16)]
    public void AcceptsTheLargestAndSmallestSupportedDimensions(int width, int height) =>
        Assert.Equal(width, FfprobeJsonParser.Parse(Json(width: width, height: height)).Width);

    [Fact]
    public void RefusesRotationsThatAreNoQuarterTurns()
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Json(
            ", \"side_data_list\": [ { \"side_data_type\": \"Display Matrix\", \"rotation\": 45 } ]")));

        Assert.Equal(VideoProbeException.UnsupportedDimensions, error.Code);
    }

    [Fact]
    public void ReadsOnlyTheFirstVideoAndAudioStream()
    {
        const string json = """
            {"streams":[
              {"codec_type":"video","codec_name":"h264","width":640,"height":360},
              {"codec_type":"video","codec_name":"h264","width":8000,"height":4000},
              {"codec_type":"audio","codec_name":"aac"},
              {"codec_type":"audio","codec_name":"mp3"}],
             "format":{"duration":"4"}}
            """;

        var probe = FfprobeJsonParser.Parse(json);

        Assert.Equal((640, 360, "aac"), (probe.Width, probe.Height, probe.AudioCodec));
    }

    private static string Streams(string video, string? audio = null, string duration = "5") =>
        "{\"streams\":[" + video + (audio is null ? "" : "," + audio) + "],\"format\":{\"duration\":\"" + duration + "\"}}";

    private const string GoodVideo = "{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640,\"height\":360}";

    [Theory]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640.5,\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":\"640\",\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":-640,\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":0,\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":99999999999,\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"height\":360}")]
    [InlineData("{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":null,\"height\":360}")]
    public void RefusesInvalidSizes(string video) =>
        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(video)));

    [Theory]
    [InlineData("1e308")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("99999999")]
    [InlineData("")]
    [InlineData(" ")]
    public void RefusesInvalidOrAbsurdDurations(string duration)
    {
        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(GoodVideo, duration: duration)));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
    }

    [Theory]
    [InlineData("1000/1", true)]
    [InlineData("241/1", true)]
    [InlineData("-30/1", true)]
    [InlineData("30/0", false)]
    [InlineData("0/0", false)]
    [InlineData("abc", false)]
    [InlineData("30000/1001", false)]
    [InlineData("240/1", false)]
    public void ValidatesTheFrameRate(string rate, bool refused)
    {
        var video = "{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640,\"height\":360,\"avg_frame_rate\":\"" + rate + "\"}";

        if (refused)
        {
            Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(video)));
        }
        else
        {
            Assert.True(FfprobeJsonParser.Parse(Streams(video)).FrameRate is null or > 0 and <= 240);
        }
    }

    [Theory]
    [InlineData("mp3", false)]
    [InlineData("opus", false)]
    [InlineData("pcm_s16le", false)]
    [InlineData("evil_codec", true)]
    [InlineData("", true)]
    public void AcceptsOnlyKnownAudioCodecs(string codec, bool refused)
    {
        var audio = "{\"codec_type\":\"audio\",\"codec_name\":\"" + codec + "\"}";

        if (refused)
        {
            var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(GoodVideo, audio)));
            Assert.Equal(VideoProbeException.UnsupportedCodec, error.Code);
        }
        else
        {
            Assert.Equal(codec, FfprobeJsonParser.Parse(Streams(GoodVideo, audio)).AudioCodec);
        }
    }

    [Fact]
    public void RefusesMoreStreamsThanAPhoneVideoHas()
    {
        var many = string.Join(',', Enumerable.Repeat("{\"codec_type\":\"data\"}", FfprobeJsonParser.MaxStreams + 1));

        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(GoodVideo, many)));
    }

    [Fact]
    public void RefusesOutputLargerThanTheLimitBeforeParsingIt()
    {
        var padding = new string(' ', FfprobeJsonParser.MaxJsonCharacters);

        var error = Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(GoodVideo) + padding));

        Assert.Equal(VideoProbeException.Unreadable, error.Code);
    }

    [Fact]
    public void RefusesDeeplyNestedJson()
    {
        var nested = new string('[', 200) + new string(']', 200);

        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse("{\"streams\":" + nested + "}"));
    }

    [Fact]
    public void RefusesManySideDataEntriesAndOverlongCodecNames()
    {
        var sideData = string.Join(',', Enumerable.Repeat("{\"rotation\":0}", 100));
        var video = "{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640,\"height\":360,\"side_data_list\":[" + sideData + "]}";
        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(video)));

        var longName = "{\"codec_type\":\"video\",\"codec_name\":\"" + new string('h', 500) + "\",\"width\":640,\"height\":360}";
        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(longName)));
    }

    [Fact]
    public void RefusesAnAbsurdRotationValue()
    {
        var video = "{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":640,\"height\":360,\"side_data_list\":[{\"rotation\":1e300}]}";

        Assert.Throws<VideoProbeException>(() => FfprobeJsonParser.Parse(Streams(video)));
    }
}
