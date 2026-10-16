using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Care;

public sealed class WaterChangeEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly EvidencePhoto[] OnePhoto = [InApp(Start.AddMinutes(5))];

    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(10);

    private static EvidencePhoto InApp(DateTimeOffset receivedAt, DateTimeOffset? takenAt = null, bool offsetKnown = true, bool reused = false) =>
        new(true, receivedAt, takenAt, offsetKnown, reused);

    private static WaterChangeEvidence Judge(DateTimeOffset? draining, DateTimeOffset? refill, DateTimeOffset finished, IReadOnlyList<EvidencePhoto> photos) =>
        WaterChangeEvidences.Evaluate(Start.AddMinutes(-1), draining, refill, finished, TenMinutes, Tolerance, photos);

    [Fact]
    public void DrainingStartedMoreThanTheMinimumBeforeTheFinish_IsVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void ExactlyTheMinimumBetweenDrainingAndTheFinish_IsVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(10), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
    }

    [Fact]
    public void OneSecondBelowTheMinimum_IsPartialAndNamesTheShortDuration()
    {
        var evidence = Judge(Start, null, Start.Add(TenMinutes).AddSeconds(-1), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void ASkippedDrainingStep_IsPartialAndNamesIt()
    {
        var evidence = Judge(null, null, Start.AddMinutes(30), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DrainingMissing], evidence.Missing);
    }

    [Fact]
    public void TheRefillStepEndsTheMeasuredSpanWhenItWasMarked()
    {
        var shortSpan = Judge(Start, Start.AddMinutes(4), Start.AddMinutes(40), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Partial, shortSpan.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], shortSpan.Missing);
        var longSpan = Judge(Start, Start.AddMinutes(12), Start.AddMinutes(13), OnePhoto);
        Assert.Equal(WaterChangeEvidenceState.Verified, longSpan.State);
    }

    [Fact]
    public void ARefillMarkedBeforeDraining_IsPartial()
    {
        var evidence = Judge(Start.AddMinutes(20), Start.AddMinutes(5), Start.AddMinutes(60), [InApp(Start.AddMinutes(30))]);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void NoPhotoAtAll_IsPartialAndNamesTheMissingPhoto()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), []);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void OnlyLibraryPhotosCountAsNoPhoto()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [new(false, Start.AddMinutes(2)), new(false, Start.AddMinutes(20))]);
        Assert.Equal([WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void OneInAppPhotoBesideALibraryPhoto_IsVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [new(false, Start.AddMinutes(2)), InApp(Start.AddMinutes(20))]);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void TwoInAppPhotos_AreVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [InApp(Start.AddMinutes(2)), InApp(Start.AddMinutes(20))]);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
    }

    [Fact]
    public void ATooShortSpanWithoutPhoto_NamesBothFindingsInOrder()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(3), []);
        Assert.Equal([WaterChangeEvidences.DurationTooShort, WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void ASkippedDrainingStepWithoutPhoto_NamesBothFindingsInOrder()
    {
        var evidence = Judge(null, null, Start.AddMinutes(30), []);
        Assert.Equal([WaterChangeEvidences.DrainingMissing, WaterChangeEvidences.PhotoMissing], evidence.Missing);
    }

    [Fact]
    public void APhotoDoesNotMakeAShortSpanVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(3), [InApp(Start.AddMinutes(1)), InApp(Start.AddMinutes(2))]);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void APhotoReceivedBeforeDrainingStarted_DoesNotCount()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [InApp(Start.AddSeconds(-1))]);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession], evidence.Missing);
    }

    [Fact]
    public void APhotoReceivedAfterTheFinish_DoesNotCount()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [InApp(Start.AddMinutes(26))]);
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession], evidence.Missing);
    }

    [Fact]
    public void APhotoReceivedExactlyWhenDrainingStarted_Counts()
    {
        Assert.Equal(WaterChangeEvidenceState.Verified, Judge(Start, null, Start.AddMinutes(25), [InApp(Start)]).State);
    }

    [Fact]
    public void APhotoWithoutADrainingStep_NamesOnlyTheDrainingFinding()
    {
        var evidence = Judge(null, null, Start.AddMinutes(25), [InApp(Start.AddMinutes(5))]);
        Assert.Equal([WaterChangeEvidences.DrainingMissing], evidence.Missing);
    }

    [Fact]
    public void OneQualifyingPhotoAmongSeveralOutsideOnes_IsVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25),
            [InApp(Start.AddSeconds(-5)), InApp(Start.AddMinutes(8)), InApp(Start.AddMinutes(30)), new(false, Start.AddMinutes(9))]);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void SixInAppPhotosInsideTheSpan_AreVerified()
    {
        var photos = Enumerable.Range(1, 6).Select(minute => InApp(Start.AddMinutes(minute))).ToList();
        Assert.Equal(WaterChangeEvidenceState.Verified, Judge(Start, null, Start.AddMinutes(25), photos).State);
    }

    [Fact]
    public void ACaptureTimeWithinTheToleranceOfTheSessionWindow_Counts()
    {
        var early = InApp(Start.AddMinutes(2), Start.AddMinutes(-1).AddMinutes(-9));
        var late = InApp(Start.AddMinutes(2), Start.AddMinutes(25).AddMinutes(9));
        Assert.Equal(WaterChangeEvidenceState.Verified, Judge(Start, null, Start.AddMinutes(25), [early]).State);
        Assert.Equal(WaterChangeEvidenceState.Verified, Judge(Start, null, Start.AddMinutes(25), [late]).State);
    }

    [Fact]
    public void ACaptureTimeBeforeTheSessionOrAfterTheFinish_DoesNotCount()
    {
        var before = InApp(Start.AddMinutes(2), Start.AddMinutes(-1).AddMinutes(-11));
        var after = InApp(Start.AddMinutes(2), Start.AddMinutes(25).AddMinutes(11));
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession], Judge(Start, null, Start.AddMinutes(25), [before]).Missing);
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession], Judge(Start, null, Start.AddMinutes(25), [after]).Missing);
    }

    [Fact]
    public void ACaptureTimeWithoutAKnownOffset_IsJudgedWithTheWidestZoneSpread()
    {
        var sameDay = InApp(Start.AddMinutes(2), Start.AddHours(-8), offsetKnown: false);
        var daysEarlier = InApp(Start.AddMinutes(2), Start.AddDays(-3), offsetKnown: false);
        Assert.Equal(WaterChangeEvidenceState.Verified, Judge(Start, null, Start.AddMinutes(25), [sameDay]).State);
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession], Judge(Start, null, Start.AddMinutes(25), [daysEarlier]).Missing);
    }

    [Fact]
    public void APhotoUsedForEarlierEvidence_IsReused()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [InApp(Start.AddMinutes(2), reused: true)]);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.PhotoReused], evidence.Missing);
    }

    [Fact]
    public void AnotherQualifyingPhotoKeepsTheEvidenceVerified()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25),
            [InApp(Start.AddMinutes(2), reused: true), InApp(Start.AddSeconds(-5)), InApp(Start.AddMinutes(20))]);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void DisqualifiedPhotosNameTheirReasonsInOrder()
    {
        var evidence = Judge(Start, null, Start.AddMinutes(25), [InApp(Start.AddMinutes(2), reused: true), InApp(Start.AddSeconds(-5))]);
        Assert.Equal([WaterChangeEvidences.PhotoOutsideSession, WaterChangeEvidences.PhotoReused], evidence.Missing);
    }
}
