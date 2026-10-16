using Cichlids.Domain;

namespace Cichlids.Api.Tests.Features.Care;

public sealed class WaterChangeEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void DrainingStartedMoreThanTheMinimumBeforeTheFinish_IsVerified()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(25), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
        Assert.Empty(evidence.Missing);
    }

    [Fact]
    public void ExactlyTheMinimumBetweenDrainingAndTheFinish_IsVerified()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.AddMinutes(10), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Verified, evidence.State);
    }

    [Fact]
    public void OneSecondBelowTheMinimum_IsPartialAndNamesTheShortDuration()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start, null, Start.Add(TenMinutes).AddSeconds(-1), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }

    [Fact]
    public void ASkippedDrainingStep_IsPartialAndNamesIt()
    {
        var evidence = WaterChangeEvidences.Evaluate(null, null, Start.AddMinutes(30), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DrainingMissing], evidence.Missing);
    }

    [Fact]
    public void TheRefillStepEndsTheMeasuredSpanWhenItWasMarked()
    {
        var shortSpan = WaterChangeEvidences.Evaluate(Start, Start.AddMinutes(4), Start.AddMinutes(40), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Partial, shortSpan.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], shortSpan.Missing);
        var longSpan = WaterChangeEvidences.Evaluate(Start, Start.AddMinutes(12), Start.AddMinutes(13), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Verified, longSpan.State);
    }

    [Fact]
    public void ARefillMarkedBeforeDraining_IsPartial()
    {
        var evidence = WaterChangeEvidences.Evaluate(Start.AddMinutes(20), Start.AddMinutes(5), Start.AddMinutes(60), TenMinutes);
        Assert.Equal(WaterChangeEvidenceState.Partial, evidence.State);
        Assert.Equal([WaterChangeEvidences.DurationTooShort], evidence.Missing);
    }
}
