using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// The public key of one app installation, registered for the provisional profile of that
/// installation. The installation proves possession of the private key by signing a one-time
/// challenge; the API holds no secret of the device. The public key is unique.
/// </summary>
public class DeviceCredential
{
    /// <summary>The identifier the installation sends with its signed requests.</summary>
    public Guid Id { get; set; }

    public long ProfileId { get; set; }

    /// <summary>The public key as DER encoded SubjectPublicKeyInfo.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>The signature algorithm of the key, ES256 (ECDSA over P-256 with SHA-256).</summary>
    public string Algorithm { get; set; } = string.Empty;

    public DeviceCredentialState State { get; set; } = DeviceCredentialState.Active;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the installation last exchanged a signed challenge for a token; the creation time until the first exchange.</summary>
    public DateTimeOffset LastSignedRequestAt { get; set; }

    /// <summary>When the credential was retired; null while it is active.</summary>
    public DateTimeOffset? RetiredAt { get; set; }
}
