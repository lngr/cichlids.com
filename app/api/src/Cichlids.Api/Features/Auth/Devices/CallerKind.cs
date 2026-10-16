using System.Security.Claims;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// Tells whether the caller of a request is registered. A caller with a device token acts for a
/// provisional profile; a caller with a Keycloak token (bearer or web session) is registered.
/// </summary>
public static class CallerKind
{
    /// <summary>The claim of a device token that marks the caller as provisional.</summary>
    public const string ProvisionalClaim = "provisional";

    /// <summary>The claim of a device token that holds the id of the device credential.</summary>
    public const string CredentialClaim = "cid";

    /// <summary>Whether the principal is authenticated with a device token.</summary>
    public static bool IsProvisional(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(ProvisionalClaim, "true");

    /// <summary>Whether the principal is authenticated and not provisional.</summary>
    public static bool IsRegistered(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && !user.IsProvisional();

    /// <summary>
    /// The Keycloak account id of a registered caller, or null for an anonymous or provisional
    /// caller. Everything that looks up the account behind an oidc identity reads the subject here,
    /// since the subject of a device token is a profile id.
    /// </summary>
    public static string? KeycloakSubject(this ClaimsPrincipal user) =>
        user.IsRegistered() ? user.FindFirst("sub")?.Value : null;

    /// <summary>The provisional profile id a device token is issued for, or null for any other caller.</summary>
    public static long? ProvisionalProfileId(this ClaimsPrincipal user) =>
        user.IsProvisional() && long.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : null;
}
