using Microsoft.Extensions.Configuration;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Connection data for the Keycloak admin REST API: the server base URL, the realm the accounts
/// belong to, and the master realm admin credentials used for the admin-cli password grant.
/// </summary>
public sealed record KeycloakAdminSettings(Uri BaseUrl, string Realm, string AdminUser, string AdminPassword)
{
    /// <summary>
    /// Reads each value from its CICHLIDS_KEYCLOAK_* environment variable, then from the Keycloak
    /// configuration section, then falls back to the local development stack (localhost:8180,
    /// realm cichlids, admin/admin).
    /// </summary>
    public static KeycloakAdminSettings Resolve(IConfiguration configuration, string environmentName) =>
        new(
            new Uri(
                Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_URL")
                ?? configuration["Keycloak:Url"]
                ?? "http://localhost:8180"),
            Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_REALM") ?? configuration["Keycloak:Realm"] ?? "cichlids",
            Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_USER") ?? configuration["Keycloak:AdminUser"] ?? "admin",
            Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD") ?? configuration["Keycloak:AdminPassword"] ?? "admin");
}
