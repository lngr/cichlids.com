using Cichlids.Infrastructure.Media;
using Cichlids.Tests.Shared;
using NetVips;

namespace Cichlids.Infrastructure.Tests;

[Collection(LibvipsCollection.Name)]
public sealed class CaptureTimeReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reads_the_original_date_and_offset_of_a_phone_photo()
    {
        var reading = CaptureTimeReader.Read(PhotoFixtures.PhoneJpeg(), Now);

        Assert.Equal(CaptureTimeOutcome.Found, reading.Outcome);
        Assert.Equal(new DateTime(2024, 5, 1, 10, 20, 30, DateTimeKind.Unspecified), reading.Time!.LocalTime);
        Assert.Equal(TimeSpan.FromHours(2), reading.Time.Offset);
    }

    [Fact]
    public void Reads_a_date_without_offset_as_wall_clock_time_with_no_offset()
    {
        var reading = CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09"), Now);

        Assert.Equal(CaptureTimeOutcome.Found, reading.Outcome);
        Assert.Equal(new DateTime(2019, 8, 17, 21, 5, 9, DateTimeKind.Unspecified), reading.Time!.LocalTime);
        Assert.Null(reading.Time.Offset);
    }

    [Theory]
    [InlineData("-05:30", -5.5)]
    [InlineData("+00:00", 0.0)]
    public void Reads_a_signed_offset(string offset, double hours)
    {
        var reading = CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09", offset), Now);

        Assert.Equal(TimeSpan.FromHours(hours), reading.Time!.Offset);
    }

    [Fact]
    public void Ignores_an_offset_that_is_not_a_valid_utc_offset()
    {
        var reading = CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime("2019:08:17 21:05:09", "+99:99"), Now);

        Assert.Equal(CaptureTimeOutcome.Found, reading.Outcome);
        Assert.Null(reading.Time!.Offset);
    }

    [Theory]
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("1989:12:31 23:59:59")]
    [InlineData("2026:10:18 12:00:00")]
    [InlineData("2019:13:40 25:61:61")]
    [InlineData("not a date")]
    public void Reports_an_implausible_date_without_a_time(string value)
    {
        var reading = CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime(value), Now);

        Assert.Equal(CaptureTimeOutcome.Implausible, reading.Outcome);
        Assert.Null(reading.Time);
    }

    [Fact]
    public void Accepts_the_first_moment_of_1990_and_a_date_up_to_a_day_ahead()
    {
        Assert.Equal(CaptureTimeOutcome.Found, CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime("1990:01:01 00:00:00"), Now).Outcome);
        Assert.Equal(CaptureTimeOutcome.Found, CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime("2026:10:17 11:00:00"), Now).Outcome);
    }

    [Fact]
    public void Reports_no_date_for_a_photo_without_exif_or_without_the_original_date()
    {
        Assert.Equal(CaptureTimeOutcome.NoDate, CaptureTimeReader.Read(PhotoFixtures.BaseJpeg(64, 48), Now).Outcome);
        Assert.Equal(CaptureTimeOutcome.NoDate, CaptureTimeReader.Read(PhotoFixtures.JpegWithCaptureTime(null, "+02:00"), Now).Outcome);
    }

    [Fact]
    public void Reports_bytes_that_are_no_image_as_unreadable()
    {
        Assert.Equal(CaptureTimeOutcome.Unreadable, CaptureTimeReader.Read("this is not an image"u8, Now).Outcome);
        Assert.Equal(CaptureTimeOutcome.Unreadable, CaptureTimeReader.Read([], Now).Outcome);
    }

    [Fact]
    public void Reads_the_date_from_the_leading_bytes_of_a_file_larger_than_they_are()
    {
        var exif = new ExifBuilder();
        exif.Exif.Add(exif.Ascii(0x9003, "2019:08:17 21:05:09"));
        using var noise = Image.Gaussnoise(1500, 1500, mean: 128, sigma: 60).Cast(Enums.BandFormat.Uchar);
        var jpeg = MetadataContainers.InsertIntoJpeg(
            noise.WriteToBuffer(".jpg", new VOption { { "Q", 95 } }),
            [MetadataContainers.JpegSegment(0xE1, MetadataContainers.ExifHeader, exif.Build())]);
        Assert.True(jpeg.Length > 2 * CaptureTimeReader.HeaderBytes, "The fixture must be larger than the header read.");

        var reading = CaptureTimeReader.Read(jpeg.AsSpan(0, CaptureTimeReader.HeaderBytes), Now);

        Assert.Equal(CaptureTimeOutcome.Found, reading.Outcome);
        Assert.Equal(new DateTime(2019, 8, 17, 21, 5, 9), reading.Time!.LocalTime);
    }

    [Fact]
    public void Reads_the_date_of_a_png_with_an_exif_chunk()
    {
        var exif = new ExifBuilder();
        exif.Exif.Add(exif.Ascii(0x9003, "2018:03:04 05:06:07"));
        var png = MetadataContainers.InsertIntoPng(
            PhotoFixtures.Flat(32, 32).WriteToBuffer(".png"), ("eXIf", exif.Build()));

        var reading = CaptureTimeReader.Read(png, Now);

        Assert.Equal(CaptureTimeOutcome.Found, reading.Outcome);
        Assert.Equal(new DateTime(2018, 3, 4, 5, 6, 7), reading.Time!.LocalTime);
    }
}
