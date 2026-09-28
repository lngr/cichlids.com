namespace Cichlids.Etl.Identity;

/// <summary>
/// A federated identity link to prepare for an account, connecting it to a social identity
/// provider by that provider's own user id.
/// </summary>
public sealed record FederatedIdentityLink(string Alias, string UserId);

/// <summary>
/// Where the login username of a planned account comes from: the legacy username, or a generated
/// name because the account has no legacy row, its legacy username holds an email address,
/// Keycloak rejects it, or another planned account keeps the same name.
/// </summary>
public enum LoginUsernameSource
{
    /// <summary>The legacy username of the account's fe_users row.</summary>
    Legacy,

    /// <summary>Generated: no fe_users row belongs to the account.</summary>
    NoLegacyRow,

    /// <summary>Generated: the legacy username contains an email address.</summary>
    EmailLike,

    /// <summary>Generated: Keycloak rejects the legacy username.</summary>
    RejectedByKeycloak,

    /// <summary>Generated: another planned account keeps the same legacy username.</summary>
    Duplicate,
}

/// <summary>
/// One Keycloak account derived from one or more merged Auth0 export records: its deterministic
/// id, login username and the generated username it falls back to, where the login username comes
/// from, optional email, required actions for first login, social identity links, and the
/// migrated profile it belongs to if one exists. GeneratedUsername equals Username when the login
/// username is itself generated.
/// </summary>
public sealed record PlannedAccount(
    Guid KeycloakUserId,
    string Username,
    string GeneratedUsername,
    LoginUsernameSource UsernameSource,
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

/// <summary>
/// A legacy member row as far as the account planning needs it: its legacy id, username, last
/// login as Unix seconds (0 when never recorded) and email.
/// </summary>
public sealed record LegacyMember(int Uid, string Username, long LastLogin, string? Email);

/// <summary>
/// Everything the account planning reads: the Auth0 export, the legacy member rows, the legacy id
/// an Auth0 subject maps to (fe_users_auth0), the migrated profile id by lowercased email and by
/// Auth0 subject (profile_identity rows with provider email and auth0), and the legacy id of every
/// migrated profile.
/// </summary>
public sealed record LegacyAccountSources(
    IReadOnlyList<Auth0ExportRecord> Auth0Records,
    IReadOnlyList<LegacyMember> LegacyMembers,
    IReadOnlyDictionary<string, int> LegacyUidBySubject,
    IReadOnlyDictionary<string, long> ProfileIdByEmail,
    IReadOnlyDictionary<string, long> ProfileIdByAuth0Subject,
    IReadOnlyDictionary<long, int> LegacyIdByProfileId);
