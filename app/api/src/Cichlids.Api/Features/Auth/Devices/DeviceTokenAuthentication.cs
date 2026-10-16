using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// The bearer scheme for device tokens. A bearer token whose issuer is the device token issuer
/// authenticates through this scheme; every other bearer token goes to the Keycloak scheme. The
/// scheme accepts only ES256 tokens signed by a key of the device token settings, found by the key
/// id of the token header, with the issuer and audience of the device tokens and a valid lifetime.
/// A token also needs an active device credential of a provisional profile, checked on every
/// request, so a retired credential ends its tokens at once.
/// </summary>
public static class DeviceTokenAuthentication
{
    public const string SchemeName = "DeviceBearer";

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Whether the request carries a bearer token that names the device token issuer.</summary>
    public static bool Applies(HttpContext context, DeviceTokenSettings settings)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var handler = new JsonWebTokenHandler();
        var token = header[prefix.Length..].Trim();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        try
        {
            return string.Equals(handler.ReadJsonWebToken(token).Issuer, settings.Issuer, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static void Configure(JwtBearerOptions options, DeviceTokenSettings settings)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ClockSkew = ClockSkew,
            // A token names its key by id; a token without an id or with an unknown one finds no key.
            IssuerSigningKeyResolver = (_, _, keyId, _) =>
                settings.ValidationKeys.Where(k => keyId is not null && k.KeyId == keyId).Select(k => (SecurityKey)k.SecurityKey).ToList(),
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = RequireActiveCredentialAsync };
    }

    private static async Task RequireActiveCredentialAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var credentialClaim = principal?.FindFirst(CallerKind.CredentialClaim)?.Value;
        if (principal is null || !principal.HasClaim(CallerKind.ProvisionalClaim, "true")
            || !Guid.TryParse(credentialClaim, out var credentialId)
            || !long.TryParse(principal.FindFirst("sub")?.Value, out var profileId))
        {
            context.Fail("The device token lacks the provisional claim, the credential or the profile.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<CichlidsDbContext>();
        var valid = await db.DeviceCredentials.AsNoTracking().AnyAsync(
            c => c.Id == credentialId && c.ProfileId == profileId && c.State == DeviceCredentialState.Active
                && db.Profiles.Any(p => p.Id == profileId && p.Kind == ProfileKind.Provisional),
            context.HttpContext.RequestAborted);
        if (!valid)
        {
            context.Fail("The device credential of the token is retired or unknown.");
        }
    }
}
