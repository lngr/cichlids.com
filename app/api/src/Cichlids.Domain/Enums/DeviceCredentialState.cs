namespace Cichlids.Domain.Enums;

/// <summary>
/// Whether a device credential authenticates its installation. A retired credential never yields
/// a token.
/// </summary>
public enum DeviceCredentialState
{
    Active,
    Retired,
}
