namespace Cichlids.Domain.Entities;

/// <summary>
/// A one-time challenge an installation signs to obtain a token. Only the SHA-256 of the random
/// challenge is stored. The row is deleted when the challenge is used, so a challenge is accepted
/// once, on any API instance, until it expires.
/// </summary>
public class DeviceChallenge
{
    public long Id { get; set; }

    /// <summary>SHA-256 of the 32 random challenge bytes; unique.</summary>
    public byte[] ChallengeHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
