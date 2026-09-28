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
    /// account's planned id holds the account's email as username or every username the account
    /// may use.
    /// </summary>
    UsernameTaken,
}

/// <summary>
/// The outcome of matching a planned account against the existing users. UserId is set for
/// Existing only; Username is set for Missing only and is the username to create the user with.
/// </summary>
public readonly record struct AccountMatch(AccountMatchKind Kind, string? UserId, string? Username = null);

/// <summary>
/// The existing users of a realm, looked up by lowercased email, by lowercased username and by
/// lowercased id.
/// </summary>
public sealed record KeycloakUserIndex(
    IReadOnlyDictionary<string, ExistingKeycloakUser> ByEmail,
    IReadOnlyDictionary<string, ExistingKeycloakUser> ByUsername,
    IReadOnlyDictionary<string, ExistingKeycloakUser> ById)
{
    /// <summary>
    /// Matches an account against the existing users. An account with an email corresponds only
    /// to the user with that email, and only when that user has a verified email or is the
    /// import's own user with the account's planned id: an unverified address does not show that
    /// the user owns it. Without a user holding its email, an account corresponds only to the
    /// user with its planned id. Any other user holding the account's email as username blocks the
    /// account, because a login with the email would reach that user. When another user holds the
    /// account's login username, an email account falls back to its generated username, since its
    /// member signs in with the email; an email-less account is blocked, because its member knows
    /// only that username.
    /// </summary>
    public AccountMatch Match(PlannedAccount account)
    {
        var plannedId = account.KeycloakUserId.ToString();

        if (account.Email is null)
        {
            if (ById.TryGetValue(plannedId.ToLowerInvariant(), out var byId))
            {
                return new AccountMatch(AccountMatchKind.Existing, byId.Id);
            }

            return IsHeldByOther(account.Username, plannedId)
                ? new AccountMatch(AccountMatchKind.UsernameTaken, null)
                : new AccountMatch(AccountMatchKind.Missing, null, account.Username);
        }

        if (ByEmail.TryGetValue(account.Email.ToLowerInvariant(), out var byEmail))
        {
            return IsPlanned(byEmail, plannedId) || byEmail.EmailVerified
                ? new AccountMatch(AccountMatchKind.Existing, byEmail.Id)
                : new AccountMatch(AccountMatchKind.ExistingUnverified, null);
        }

        if (ById.TryGetValue(plannedId.ToLowerInvariant(), out var ownUser))
        {
            return new AccountMatch(AccountMatchKind.Existing, ownUser.Id);
        }

        if (IsHeldByOther(account.Email, plannedId))
        {
            return new AccountMatch(AccountMatchKind.UsernameTaken, null);
        }

        foreach (var username in new[] { account.Username, account.GeneratedUsername })
        {
            if (!IsHeldByOther(username, plannedId))
            {
                return new AccountMatch(AccountMatchKind.Missing, null, username);
            }
        }

        return new AccountMatch(AccountMatchKind.UsernameTaken, null);
    }

    private bool IsHeldByOther(string username, string plannedId) =>
        ByUsername.TryGetValue(KeycloakUsernameRule.Normalize(username), out var holder) && !IsPlanned(holder, plannedId);

    private static bool IsPlanned(ExistingKeycloakUser user, string plannedId) =>
        string.Equals(user.Id, plannedId, StringComparison.OrdinalIgnoreCase);
}
