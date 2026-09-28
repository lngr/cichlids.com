namespace Cichlids.Etl.Identity;

/// <summary>
/// An existing Keycloak user as far as the account import needs it: its id and whether its
/// email is verified.
/// </summary>
public sealed record ExistingKeycloakUser(string Id, bool EmailVerified);

/// <summary>
/// How a planned account relates to the existing users of a realm.
/// </summary>
public enum AccountMatchKind
{
    /// <summary>No existing user corresponds to the account.</summary>
    Missing,

    /// <summary>An existing user is the account's user and the account links to it.</summary>
    Existing,

    /// <summary>
    /// The account's email belongs to a user the import did not create and whose email is
    /// unverified.
    /// </summary>
    ExistingUnverified,

    /// <summary>
    /// No user has the account's email, or the account has none, while a user without the
    /// account's planned id already has the account's username.
    /// </summary>
    UsernameTaken,
}

/// <summary>
/// The outcome of matching a planned account against the existing users; UserId is set for
/// Existing only.
/// </summary>
public readonly record struct AccountMatch(AccountMatchKind Kind, string? UserId);

/// <summary>
/// The existing users of a realm, looked up by lowercased email and by lowercased username.
/// </summary>
public sealed record KeycloakUserIndex(
    IReadOnlyDictionary<string, ExistingKeycloakUser> ByEmail,
    IReadOnlyDictionary<string, ExistingKeycloakUser> ByUsername)
{
    /// <summary>
    /// Matches an account against the existing users. An account with an email corresponds only
    /// to the user with that email, and only when that user has a verified email or is the
    /// import's own user with the account's planned id: an unverified address does not show
    /// that the user owns it. Otherwise the account corresponds to the user with its username
    /// only when that user has the account's planned id. Any other user holding the account's
    /// username blocks the account, because anyone can register that username.
    /// </summary>
    public AccountMatch Match(PlannedAccount account)
    {
        var plannedId = account.KeycloakUserId.ToString();

        if (account.Email is not null && ByEmail.TryGetValue(account.Email.ToLowerInvariant(), out var byEmail))
        {
            return IsPlanned(byEmail, plannedId) || byEmail.EmailVerified
                ? new AccountMatch(AccountMatchKind.Existing, byEmail.Id)
                : new AccountMatch(AccountMatchKind.ExistingUnverified, null);
        }

        if (ByUsername.TryGetValue(account.Username.ToLowerInvariant(), out var byUsername))
        {
            return IsPlanned(byUsername, plannedId)
                ? new AccountMatch(AccountMatchKind.Existing, byUsername.Id)
                : new AccountMatch(AccountMatchKind.UsernameTaken, null);
        }

        return new AccountMatch(AccountMatchKind.Missing, null);
    }

    private static bool IsPlanned(ExistingKeycloakUser user, string plannedId) =>
        string.Equals(user.Id, plannedId, StringComparison.OrdinalIgnoreCase);
}
