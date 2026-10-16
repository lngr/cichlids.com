namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>The public key to register: DER encoded SubjectPublicKeyInfo of an ECDSA P-256 key, base64url.</summary>
public sealed record RegisterDeviceKeyRequest(string? PublicKey);

/// <summary>The device credential of a registered key and the provisional profile it belongs to.</summary>
public sealed record DeviceCredentialDto(Guid CredentialId, long ProfileId, string DisplayName);

/// <summary>A one-time challenge (32 random bytes, base64url) and the moment it expires.</summary>
public sealed record DeviceChallengeDto(string Challenge, DateTimeOffset ExpiresAt);

/// <summary>
/// A signed challenge: the challenge as issued, the credential id, the timestamp in whole seconds
/// since 1970-01-01T00:00:00Z and the signature (r and s, 64 bytes, base64url) over the message of
/// DeviceSignatureMessage.
/// </summary>
public sealed record DeviceTokenRequest(string? Challenge, Guid? CredentialId, long? Timestamp, string? Signature);

/// <summary>A device access token; it expires fifteen minutes after issue and has no refresh token.</summary>
public sealed record DeviceTokenDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
