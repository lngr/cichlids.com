using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>An issued device token and the moment it expires.</summary>
public sealed record IssuedDeviceToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues the access tokens of provisional profiles, signed with the current key of the device
/// token settings (ES256, with the key id in the header). The subject is the id of the provisional
/// profile, the provisional claim marks the caller, and the credential claim names the device
/// credential the token was issued for.
/// </summary>
public sealed class DeviceTokenIssuer(DeviceTokenSettings settings)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedDeviceToken Issue(Guid credentialId, long profileId, DateTimeOffset now)
    {
        var expires = now + DeviceTokenSettings.TokenLifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.Current.SecurityKey, SecurityAlgorithms.EcdsaSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", profileId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(CallerKind.ProvisionalClaim, "true", ClaimValueTypes.String),
                new Claim(CallerKind.CredentialClaim, credentialId.ToString("D")),
                new Claim("jti", Guid.NewGuid().ToString("N")),
            ]),
        };

        return new IssuedDeviceToken(_handler.CreateToken(descriptor), expires);
    }
}
