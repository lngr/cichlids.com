using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Promotes Keycloak's realm role claim into standard role claims. Keycloak encodes realm role
/// membership as a JSON object claim (realm_access with a roles array) that ASP.NET Core's role
/// checks cannot read directly, so each entry is re-added under the identity's role claim type.
/// </summary>
public static class KeycloakRealmRoles
{
    public static Task PromoteToRoleClaims(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return Task.CompletedTask;
        }

        var realmAccess = identity.FindFirst("realm_access")?.Value;
        if (string.IsNullOrEmpty(realmAccess))
        {
            return Task.CompletedTask;
        }

        using var document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return Task.CompletedTask;
        }

        foreach (var role in roles.EnumerateArray())
        {
            if (role.GetString() is { Length: > 0 } value)
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, value));
            }
        }

        return Task.CompletedTask;
    }
}
