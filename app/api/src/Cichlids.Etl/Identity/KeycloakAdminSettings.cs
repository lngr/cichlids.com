using Microsoft.Extensions.Configuration;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Connection data for the Keycloak admin REST API: the server base URL, the realm the accounts
/// belong to, and the master realm admin credentials used for the admin-cli password grant.
/// </summary>
public sealed record KeycloakAdminSettings(Uri BaseUrl, string Realm, string AdminUser, string AdminPassword)
{
    private const string DevelopmentEnvironmentName = "Development";
    private const string LocalAdmin = "admin";

    /// <summary>
    /// Reads each value from its CICHLIDS_KEYCLOAK_* environment variable, then from the Keycloak
    /// configuration section, then falls back to the local development stack (localhost:8180,
    /// realm cichlids). The admin credentials fall back to admin/admin in the Development
    /// environment only. Outside Development, missing admin credentials or the admin/admin pair
    /// raise an exception naming the environment variables and configuration keys to set.
    /// </summary>
    public static KeycloakAdminSettings Resolve(IConfiguration configuration, string environmentName)
    {
        var user = FirstNonEmpty(Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_USER"), configuration["Keycloak:AdminUser"]);
        var password = FirstNonEmpty(Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD"), configuration["Keycloak:AdminPassword"]);

        if (string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            user ??= LocalAdmin;
            password ??= LocalAdmin;
        }
        else if (user is null || password is null || (user == LocalAdmin && password == LocalAdmin))
        {
            throw new InvalidOperationException(
                $"No Keycloak admin credentials configured for environment '{environmentName}'. The local "
                + "admin/admin pair is accepted in the Development environment only. Set "
                + "CICHLIDS_KEYCLOAK_ADMIN_USER and CICHLIDS_KEYCLOAK_ADMIN_PASSWORD (or Keycloak:AdminUser "
                + "and Keycloak:AdminPassword) to the credentials of the realm administrator.");
        }

        return new KeycloakAdminSettings(
            new Uri(
                FirstNonEmpty(Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_URL"), configuration["Keycloak:Url"])
                ?? "http://localhost:8180"),
            FirstNonEmpty(Environment.GetEnvironmentVariable("CICHLIDS_KEYCLOAK_REALM"), configuration["Keycloak:Realm"]) ?? "cichlids",
            user,
            password);
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrEmpty(first) ? first : (!string.IsNullOrEmpty(second) ? second : null);
}
