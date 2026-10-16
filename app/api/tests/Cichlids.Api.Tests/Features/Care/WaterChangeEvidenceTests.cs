using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Care;

public sealed class WaterChangeEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private const int OnePhoto = 1;

    [Fact]
    public void DrainingStartedMoreThanTheMinimumBeforeTheFinish_IsVerified()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void ExactlyTheMinimumBetweenDrainingAndTheFinish_IsVerified()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(10), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
    }

    [Fact]
    public void OneSecondBelowTheMinimum_IsPartialAndNamesTheShortDuration()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.Add(TenMinutes).AddSeconds(-1), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void ASkippedDrainingStep_IsPartialAndNamesIt()
    {
        var evidence = WaterChangeEvidences.Evaluate(null, null, Start.AddMinutes(30), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DrainingMissing], evidence.Missing);
    }

    [Fact]
    public void TheRefillStepEndsTheMeasuredSpanWhenItWasMarked()
    {
        var shortSpan = WaterChangeEvidences.Evaluate(Start, Start.AddMinutes(4), Start.AddMinutes(40), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, shortSpan.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], shortSpan.Missing);
        var longSpan = WaterChangeEvidences.Evaluate(Start, Start.AddMinutes(12), Start.AddMinutes(13), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, longSpan.State);
    }

    [Fact]
    public void ARefillMarkedBeforeDraining_IsPartial()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start.AddMinutes(20), Start.AddMinutes(5), Start.AddMinutes(60), TenMinutes, OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void NoPhotoAtAll_IsPartialAndNamesTheMissingPhoto()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes, 0);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void OnlyLibraryPhotosCountAsNoPhoto()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes, WaterChangeEvidences.CountInAppPhotos(false, false));
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void OneInAppPhotoBesideALibraryPhoto_IsVerified()
    {
        var count = WaterChangeEvidences.CountInAppPhotos(true, false);
        Assert.Equal(1, count);
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes, count);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void TwoInAppPhotos_AreVerified()
    {
        var count = WaterChangeEvidences.CountInAppPhotos(true, true);
        Assert.Equal(2, count);
        Assert.Equal(WaterChangeEvidenceState.Verified, WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes, count).State);
    }

    [Fact]
    public void ATooShortSpanWithoutPhoto_NamesBothFindingsInOrder()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(3), TenMinutes, 0);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort, WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void ASkippedDrainingStepWithoutPhoto_NamesBothFindingsInOrder()
    {
        var evidence = WaterChangeEvidences.Evaluate(null, null, Start.AddMinutes(30), TenMinutes, 0);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DrainingMissing, WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void APhotoDoesNotMakeAShortSpanVerified()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(3), TenMinutes, 2);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }
}
