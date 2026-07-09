using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Tests;

/// <summary>
/// Issues JWTs for authenticated-endpoint tests, signed with a key generated once per test run.
/// The fixture overrides the API's token validation parameters to accept exactly these tokens, so
/// no test ever needs a running identity provider.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://keycloak.test/realms/cichlids";
    public const string Audience = "cichlids-api";

    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Validation parameters matching the tokens this class issues. The claim type names mirror
    /// the production configuration (preferred_username as the name claim), so the resolution
    /// logic under test reads the same claims it reads against a real Keycloak.
    /// </summary>
    public static TokenValidationParameters ValidationParameters => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey,
        NameClaimType = "preferred_username",
    };

    public static string Create(
        string subject, string username, string? name = null, string? email = null, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("preferred_username", username),
        };

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims),
        };

        if (roles.Length > 0)
        {
            // Shaped exactly like Keycloak's realm role claim, so the API's claim promotion
            // (realm_access.roles into role claims) is exercised rather than bypassed.
            descriptor.Claims = new Dictionary<string, object>
            {
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles },
            };
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
