using Cichlids.Infrastructure.Identity;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Derives the Keycloak accounts to create from an Auth0 export, merging records that resolve to
/// the same email, resolving migrated profile links and assigning login usernames. Pure
/// computation: no database or HTTP call is made here.
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
    /// Plans the Keycloak accounts for the given sources, deriving generated login usernames with
    /// the given generator.
    /// </summary>
    public static AccountPlan Plan(LegacyAccountSources sources, GeneratedNames generatedNames)
    {
        var membersByUid = sources.LegacyMembers.ToDictionary(member => member.Uid);
        var membersByEmail = sources.LegacyMembers
            .Where(member => NormalizeEmail(member.Email) is not null)
            .ToLookup(member => NormalizeEmail(member.Email)!, StringComparer.Ordinal);
        var legacyEmailBySubject = sources.LegacyUidBySubject
            .Where(pair => membersByUid.TryGetValue(pair.Value, out var member) && NormalizeEmail(member.Email) is not null)
            .ToDictionary(pair => pair.Key, pair => NormalizeEmail(membersByUid[pair.Value].Email)!, StringComparer.Ordinal);

        var groupOrder = new List<string>();
        var groups = new Dictionary<string, List<Auth0ExportRecord>>(StringComparer.Ordinal);
        var emailless = new List<Auth0ExportRecord>();

        foreach (var record in sources.Auth0Records)
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

        var drafts = new List<AccountDraft>();
        foreach (var key in groupOrder)
        {
            var draft = BuildEmailAccount(key, groups[key], sources.ProfileIdByEmail, sources.ProfileIdByAuth0Subject);
            drafts.Add(new AccountDraft(key, draft, FindLegacyRow(draft, groups[key], membersByUid, membersByEmail, sources)));
        }

        var droppedNoContactNoProfile = 0;
        foreach (var record in emailless)
        {
            if (sources.ProfileIdByAuth0Subject.TryGetValue(record.Id, out var profileId))
            {
                var draft = BuildEmaillessAccount(record, profileId);
                drafts.Add(new AccountDraft(record.Id, draft, FindLegacyRow(draft, [record], membersByUid, membersByEmail, sources)));
            }
            else
            {
                droppedNoContactNoProfile++;
            }
        }

        return new AccountPlan(AssignUsernames(drafts, generatedNames), droppedNoContactNoProfile);
    }

    /// <summary>
    /// Gives every account its login username. An account keeps the legacy username of its row
    /// when that holds no email address, Keycloak accepts it and no other account keeps the same
    /// name in any letter case; among accounts sharing a name, the one with a migrated profile
    /// wins, then the one whose row logged in last, then the one with the lowest legacy id. Every
    /// account also gets a generated name keyed by its account key, unique against all kept and
    /// generated names, which is its login username when it keeps no legacy name.
    /// </summary>
    private static List<PlannedAccount> AssignUsernames(List<AccountDraft> drafts, GeneratedNames generatedNames)
    {
        var sourceByDraft = new Dictionary<AccountDraft, LoginUsernameSource>(ReferenceEqualityComparer.Instance);
        var candidates = new List<AccountDraft>();
        foreach (var draft in drafts)
        {
            var username = draft.LegacyRow?.Username;
            if (username is null)
            {
                sourceByDraft[draft] = LoginUsernameSource.NoLegacyRow;
            }
            else if (EmailLike.Contains(username))
            {
                sourceByDraft[draft] = LoginUsernameSource.EmailLike;
            }
            else if (!KeycloakUsernameRule.IsAccepted(username))
            {
                sourceByDraft[draft] = LoginUsernameSource.RejectedByKeycloak;
            }
            else
            {
                candidates.Add(draft);
            }
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sameName in candidates.GroupBy(draft => KeycloakUsernameRule.Normalize(draft.LegacyRow!.Username), StringComparer.Ordinal))
        {
            var ranked = sameName
                .OrderBy(draft => draft.Account.ProfileId is null ? 1 : 0)
                .ThenByDescending(draft => draft.LegacyRow!.LastLogin)
                .ThenBy(draft => draft.LegacyRow!.Uid)
                .ThenBy(draft => draft.Key, StringComparer.Ordinal)
                .ToList();

            sourceByDraft[ranked[0]] = LoginUsernameSource.Legacy;
            taken.Add(sameName.Key);
            foreach (var loser in ranked.Skip(1))
            {
                sourceByDraft[loser] = LoginUsernameSource.Duplicate;
            }
        }

        var generatedByDraft = new Dictionary<AccountDraft, string>(ReferenceEqualityComparer.Instance);
        foreach (var draft in drafts.OrderBy(draft => draft.Key, StringComparer.Ordinal))
        {
            var generated = generatedNames.GenerateUnique(
                GeneratedNames.LoginInput(draft.Key), name => taken.Contains(KeycloakUsernameRule.Normalize(name)));
            taken.Add(KeycloakUsernameRule.Normalize(generated));
            generatedByDraft[draft] = generated;
        }

        return drafts
            .Select(draft =>
            {
                var source = sourceByDraft[draft];
                var generated = generatedByDraft[draft];
                return draft.Account with
                {
                    Username = source == LoginUsernameSource.Legacy ? draft.LegacyRow!.Username : generated,
                    GeneratedUsername = generated,
                    UsernameSource = source,
                };
            })
            .ToList();
    }

    // The account's legacy rows are the rows sharing its email and the rows its Auth0 subjects map
    // to. The row of its migrated profile comes first, then the row that logged in last, then the
    // lowest legacy id.
    private static LegacyMember? FindLegacyRow(
        PlannedAccount account,
        IReadOnlyList<Auth0ExportRecord> records,
        IReadOnlyDictionary<int, LegacyMember> membersByUid,
        ILookup<string, LegacyMember> membersByEmail,
        LegacyAccountSources sources)
    {
        var rows = new Dictionary<int, LegacyMember>();
        if (account.Email is not null)
        {
            foreach (var member in membersByEmail[account.Email])
            {
                rows[member.Uid] = member;
            }
        }

        foreach (var record in records)
        {
            if (sources.LegacyUidBySubject.TryGetValue(record.Id, out var uid) && membersByUid.TryGetValue(uid, out var member))
            {
                rows[member.Uid] = member;
            }
        }

        if (account.ProfileId is { } profileId
            && sources.LegacyIdByProfileId.TryGetValue(profileId, out var profileUid)
            && rows.TryGetValue(profileUid, out var profileRow))
        {
            return profileRow;
        }

        return rows.Values
            .OrderByDescending(member => member.LastLogin)
            .ThenBy(member => member.Uid)
            .FirstOrDefault();
    }

    private sealed record AccountDraft(string Key, PlannedAccount Account, LegacyMember? LegacyRow);

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
            string.Empty,
            string.Empty,
            LoginUsernameSource.NoLegacyRow,
            key,
            members.Any(member => member.EmailVerified),
            requiredActions.ToList(),
            BuildFederatedIdentities(members),
            ResolveProfileId(key, members, profileIdByEmail, profileIdByAuth0Subject));
    }

    private static PlannedAccount BuildEmaillessAccount(Auth0ExportRecord record, long profileId)
    {
        var requiredActions = new SortedSet<string>(StringComparer.Ordinal) { TermsAndConditionsAction };
        if (string.Equals(record.Connection, PasswordConnection, StringComparison.Ordinal))
        {
            requiredActions.Add(UpdatePasswordAction);
        }

        return new PlannedAccount(
            Uuidv5.Create(NamespaceId, record.Id),
            string.Empty,
            string.Empty,
            LoginUsernameSource.NoLegacyRow,
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
