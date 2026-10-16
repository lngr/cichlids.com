namespace Cichlids.Domain.Enums;

/// <summary>
/// The way a photo was used as proof. WaterChangeEvidence marks a photo of a finished water change
/// session. Proof marks a photo of a care entry or of a decommissioning or recommissioning of a tank.
/// </summary>
public enum UsedChecksumKind
{
    WaterChangeEvidence,
    Proof,
}
