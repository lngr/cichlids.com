namespace Cichlids.Etl.Identity;

/// <summary>
/// The identity providers the profile step writes from legacy data. The step replaces the
/// identities of these providers on a rerun; identities of any other provider (such as oidc, which
/// the account import links) are never touched. The email provider is included because only the
/// profile step writes it, from the legacy email address.
/// </summary>
public static class LegacyIdentityProviders
{
    public const string Auth0 = "auth0";
    public const string LegacyOpenId = "legacy-openid";
    public const string Email = "email";

    public static IReadOnlyList<string> All { get; } = [Auth0, LegacyOpenId, Email];
}
