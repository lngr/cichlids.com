using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Decommissioning;

/// <summary>
/// The photo that proves a decommissioning has to come from the camera of the app, show a plausible
/// capture time and be new.
/// </summary>
[Trait("Story", "task-6")]
public sealed class DecommissionProofTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(10);

    private static string? Check(bool inApp = true, DateTimeOffset? taken = null, bool offsetKnown = true, bool reused = false) =>
        DecommissionProofs.Check(new EvidencePhoto(inApp, Received, taken, offsetKnown, reused), Tolerance);

    [Fact]
    public void APhotoFromTheAppWithoutCaptureTimeQualifies() => Assert.Null(Check());

    [Fact]
    public void APhotoNotTakenWithTheAppCameraIsDisqualifiedFirst() =>
        Assert.Equal(DecommissionProofs.NotInApp, Check(inApp: false, taken: Received.AddDays(-3), reused: true));

    [Theory]
    [InlineData(-9, null)]
    [InlineData(9, null)]
    [InlineData(-11, DecommissionProofs.TimeImplausible)]
    [InlineData(11, DecommissionProofs.TimeImplausible)]
    public void TheCaptureTimeHasToLieWithinTheToleranceOfTheMomentOfReceipt(int minutes, string? expected) =>
        Assert.Equal(expected, Check(taken: Received.AddMinutes(minutes)));

    [Theory]
    [InlineData(13 * 60, null)]
    [InlineData(-14 * 60 - 9, null)]
    [InlineData(15 * 60, DecommissionProofs.TimeImplausible)]
    public void ACaptureTimeWithoutAKnownOffsetGetsTheWidestZoneSpreadOnTop(int minutes, string? expected) =>
        Assert.Equal(expected, Check(taken: Received.AddMinutes(minutes), offsetKnown: false));

    [Fact]
    public void ThePictureOfAnEarlierProofIsDisqualified() => Assert.Equal(DecommissionProofs.Reused, Check(reused: true));
}
