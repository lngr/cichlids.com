namespace Cichlids.Etl.Identity;

/// <summary>
/// A federated identity link to prepare for an account, connecting it to a social identity
/// provider by that provider's own user id.
/// </summary>
public sealed record FederatedIdentityLink(string Alias, string UserId);

/// <summary>
/// One Keycloak account derived from one or more merged Auth0 export records: its deterministic
/// id, login name, optional email, required actions for first login, social identity links, and
/// the migrated profile it belongs to if one exists.
/// </summary>
public sealed record PlannedAccount(
    Guid KeycloakUserId,
    string Username,
    string? Email,
    bool EmailVerified,
    IReadOnlyList<string> RequiredActions,
    IReadOnlyList<FederatedIdentityLink> FederatedIdentities,
    long? ProfileId);

/// <summary>
/// The accounts to create in Keycloak, plus the count of Auth0 export records dropped for having
/// neither an email nor a migrated profile.
/// </summary>
public sealed record AccountPlan(IReadOnlyList<PlannedAccount> Accounts, int DroppedNoContactNoProfile);
