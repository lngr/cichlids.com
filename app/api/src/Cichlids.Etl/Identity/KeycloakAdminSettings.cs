namespace Cichlids.Etl.Identity;

/// <summary>
/// Connection data for the Keycloak admin REST API: the server base URL, the realm the accounts
/// belong to, and the master realm admin credentials used for the admin-cli password grant.
/// </summary>
public sealed record KeycloakAdminSettings(Uri BaseUrl, string Realm, string AdminUser, string AdminPassword);
