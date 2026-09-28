namespace Cichlids.Etl.Identity;

/// <summary>
/// Derives the Keycloak accounts to create from an Auth0 export, merging records that resolve to
/// the same email and resolving migrated profile links. Pure computation: no database or HTTP
/// call is made here.
/// </summary>
public static class LegacyAccountPlanner
{
    /// <summary>
    /// Namespace for the deterministic account ids this planner assigns, so that an id depends
    /// only on an account's key and is identical across repeated runs.
    /// </summary>
    public static readonly Guid NamespaceId = Guid.Parse("339d5db6-702c-487c-8fff-d5b53db4b100");

    private const string PasswordConnection = "Username-Password-Authentication";
    private const string UpdatePasswordAction = "UPDATE_PASSWORD";
    private const string TermsAndConditionsAction = "TERMS_AND_CONDITIONS";

    private static readonly IReadOnlyDictionary<string, string> FederatedProviderAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["google-oauth2"] = "google",
            ["facebook"] = "facebook",
        };

    /// <summary>
    /// Plans the Keycloak accounts for the given Auth0 records.
    /// </summary>
    /// <param name="auth0Records">The Auth0 export.</param>
    /// <param name="legacyEmailBySubject">The legacy email for an Auth0 subject, from fe_users_auth0 joined with fe_users, containing only subjects with a non-empty legacy email.</param>
    /// <param name="profileIdByEmail">The migrated profile id for a lowercased email, from a profile_identity row with provider email.</param>
    /// <param name="profileIdByAuth0Subject">The migrated profile id for an Auth0 subject, from a profile_identity row with provider auth0.</param>
    public static AccountPlan Plan(
        IReadOnlyList<Auth0ExportRecord> auth0Records,
        IReadOnlyDictionary<string, string> legacyEmailBySubject,
        IReadOnlyDictionary<string, long> profileIdByEmail,
        IReadOnlyDictionary<string, long> profileIdByAuth0Subject)
    {
        var groupOrder = new List<string>();
        var groups = new Dictionary<string, List<Auth0ExportRecord>>(StringComparer.Ordinal);
        var emailless = new List<Auth0ExportRecord>();

        foreach (var record in auth0Records)
        {
            var key = ResolveEmailKey(record, legacyEmailBySubject);
            if (key is null)
            {
                emailless.Add(record);
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
                groupOrder.Add(key);
            }

            group.Add(record);
        }

        var accounts = new List<PlannedAccount>();
        foreach (var key in groupOrder)
        {
            accounts.Add(BuildEmailAccount(key, groups[key], profileIdByEmail, profileIdByAuth0Subject));
        }

        var droppedNoContactNoProfile = 0;
        foreach (var record in emailless)
        {
            if (profileIdByAuth0Subject.TryGetValue(record.Id, out var profileId))
            {
                accounts.Add(BuildEmaillessAccount(record, profileId));
            }
            else
            {
                droppedNoContactNoProfile++;
            }
        }

        return new AccountPlan(accounts, droppedNoContactNoProfile);
    }

    // An account's key is its own email if present, otherwise the legacy email reachable through
    // its Auth0 subject; a record with neither has no email key at all.
    private static string? ResolveEmailKey(Auth0ExportRecord record, IReadOnlyDictionary<string, string> legacyEmailBySubject)
    {
        var ownEmail = NormalizeEmail(record.Email);
        if (ownEmail is not null)
        {
            return ownEmail;
        }

        return legacyEmailBySubject.TryGetValue(record.Id, out var legacyEmail)
            ? NormalizeEmail(legacyEmail)
            : null;
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static PlannedAccount BuildEmailAccount(
        string key,
        List<Auth0ExportRecord> members,
        IReadOnlyDictionary<string, long> profileIdByEmail,
        IReadOnlyDictionary<string, long> profileIdByAuth0Subject)
    {
        var requiredActions = new SortedSet<string>(StringComparer.Ordinal) { TermsAndConditionsAction };
        if (members.Any(member => string.Equals(member.Connection, PasswordConnection, StringComparison.Ordinal)))
        {
            requiredActions.Add(UpdatePasswordAction);
        }

        return new PlannedAccount(
            Uuidv5.Create(NamespaceId, key),
            key,
            key,
            members.Any(member => member.EmailVerified),
            requiredActions.ToList(),
            BuildFederatedIdentities(members),
            ResolveProfileId(key, members, profileIdByEmail, profileIdByAuth0Subject));
    }

    private static PlannedAccount BuildEmaillessAccount(Auth0ExportRecord record, long profileId)
    {
        var (prefix, userId) = SplitSubject(record.Id);
        var alias = FederatedProviderAliases.TryGetValue(prefix, out var mappedAlias) ? mappedAlias : prefix;

        var requiredActions = new SortedSet<string>(StringComparer.Ordinal) { TermsAndConditionsAction };
        if (string.Equals(record.Connection, PasswordConnection, StringComparison.Ordinal))
        {
            requiredActions.Add(UpdatePasswordAction);
        }

        return new PlannedAccount(
            Uuidv5.Create(NamespaceId, record.Id),
            $"{alias}-{userId}",
            null,
            record.EmailVerified,
            requiredActions.ToList(),
            BuildFederatedIdentities([record]),
            profileId);
    }

    private static long? ResolveProfileId(
        string key,
        List<Auth0ExportRecord> members,
        IReadOnlyDictionary<string, long> profileIdByEmail,
        IReadOnlyDictionary<string, long> profileIdByAuth0Subject)
    {
        if (profileIdByEmail.TryGetValue(key, out var profileIdViaEmail))
        {
            return profileIdViaEmail;
        }

        foreach (var member in members)
        {
            if (profileIdByAuth0Subject.TryGetValue(member.Id, out var profileIdViaAuth0))
            {
                return profileIdViaAuth0;
            }
        }

        return null;
    }

    // A record whose own email is unverified joins an account only through an address its owner
    // never confirmed, so its social login must not reach that account. A record without an email
    // of its own belongs to its account through its legacy subject.
    private static List<FederatedIdentityLink> BuildFederatedIdentities(IReadOnlyList<Auth0ExportRecord> members)
    {
        var links = new HashSet<(string Alias, string UserId)>();
        foreach (var member in members)
        {
            if (!member.EmailVerified && NormalizeEmail(member.Email) is not null)
            {
                continue;
            }

            var (prefix, userId) = SplitSubject(member.Id);
            if (FederatedProviderAliases.TryGetValue(prefix, out var alias))
            {
                links.Add((alias, userId));
            }
        }

        return links
            .OrderBy(link => link.Alias, StringComparer.Ordinal)
            .ThenBy(link => link.UserId, StringComparer.Ordinal)
            .Select(link => new FederatedIdentityLink(link.Alias, link.UserId))
            .ToList();
    }

    // An Auth0 subject is "<connection prefix>|<provider user id>", e.g. "google-oauth2|1234".
    private static (string Prefix, string UserId) SplitSubject(string subject)
    {
        var separatorIndex = subject.IndexOf('|');
        return separatorIndex < 0
            ? (subject, string.Empty)
            : (subject[..separatorIndex], subject[(separatorIndex + 1)..]);
    }
}
