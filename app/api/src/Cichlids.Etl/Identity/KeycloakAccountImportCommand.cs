using Cichlids.Infrastructure.Identity;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Identity;

/// <summary>
/// The "keycloak-import" command: creates a Keycloak user for every legacy account from the
/// Auth0 export that has a known email or a migrated profile, with the login username the
/// planner assigns, and links each account with a migrated profile to that profile through an
/// oidc profile identity whose subject is the Keycloak user id. Existing Keycloak users are never
/// changed. An account whose email belongs to an existing user with a verified email is linked to
/// that user. An email account whose login username another user holds is created with its
/// generated username. An account whose email belongs to an existing user with an unverified
/// email, whose email another user holds as username, or which has no free username left is
/// neither created nor linked and is counted as a conflict. A repeated run over the same data
/// creates no user and inserts no identity.
/// </summary>
public static class KeycloakAccountImportCommand
{
    private const string OidcProvider = "oidc";
    private const string EmailProvider = "email";
    private const string Auth0Provider = "auth0";
    private const string DroppedNoContactNoProfile = "no_contact_no_profile";
    private const string ExistingUnverifiedConflict = "existing_unverified";
    private const string UsernameTakenConflict = "username_taken";

    /// <summary>
    /// Runs the import and prints its summary. Returns 0 on success and 1 when an account could
    /// not be found in Keycloak after the import.
    /// </summary>
    public static async Task<int> RunAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string auth0ExportPath,
        KeycloakAdminSettings keycloak,
        GeneratedNames generatedNames,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Importing legacy accounts from {auth0ExportPath} into Keycloak realm '{keycloak.Realm}' at {keycloak.BaseUrl}...");

        var summary = await ImportAsync(
            legacyConnectionString, targetConnectionString, auth0ExportPath, keycloak, generatedNames, cancellationToken);

        Console.WriteLine($"Auth0 records read:        {summary.RecordsRead}");
        Console.WriteLine($"Accounts planned:          {summary.AccountsPlanned}");
        foreach (var (source, count) in summary.LoginUsernamesBySource)
        {
            Console.WriteLine($"  login username ({source}): {count}");
        }

        Console.WriteLine($"  created:                 {summary.Created}");
        Console.WriteLine($"  existing:                {summary.Existing}");
        Console.WriteLine($"  generated for a taken username: {summary.GeneratedForTakenUsername}");
        foreach (var (reason, count) in summary.DroppedByReason)
        {
            Console.WriteLine($"Records dropped ({reason}): {count}");
        }

        foreach (var (reason, count) in summary.AccountConflictsByReason)
        {
            Console.WriteLine($"Accounts skipped ({reason}): {count}");
        }

        Console.WriteLine($"Profile links inserted:    {summary.ProfileLinksInserted}");
        Console.WriteLine($"Profile links present:     {summary.ProfileLinksPresent}");
        Console.WriteLine($"Profile link conflicts:    {summary.ProfileLinkConflicts}");

        if (summary.Unresolved > 0)
        {
            Console.Error.WriteLine($"Accounts missing in Keycloak after the import: {summary.Unresolved}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Plans the accounts from the Auth0 export, the legacy user table and the migrated profiles,
    /// ensures the realm prerequisites, creates the missing Keycloak users and inserts the missing
    /// oidc profile identities. Generated login usernames come from the given generator.
    /// </summary>
    public static Task<KeycloakImportSummary> ImportAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string auth0ExportPath,
        KeycloakAdminSettings keycloak,
        GeneratedNames generatedNames,
        CancellationToken cancellationToken) =>
        ImportAsync(
            legacyConnectionString,
            targetConnectionString,
            auth0ExportPath,
            keycloak,
            generatedNames,
            KeycloakAdminClient.DefaultUserPageSize,
            KeycloakAdminClient.DefaultImportChunkSize,
            cancellationToken);

    /// <summary>
    /// Runs the import with the given number of users per user list page and per partial import
    /// request.
    /// </summary>
    internal static async Task<KeycloakImportSummary> ImportAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string auth0ExportPath,
        KeycloakAdminSettings keycloak,
        GeneratedNames generatedNames,
        int userPageSize,
        int importChunkSize,
        CancellationToken cancellationToken)
    {
        var records = Auth0ExportReader.ReadFile(auth0ExportPath);
        var (members, uidBySubject) = await LoadLegacyMembersAsync(legacyConnectionString, cancellationToken);
        var identities = await LoadProfileIdentitiesAsync(targetConnectionString, cancellationToken);
        var legacyIdByProfileId = await LoadProfileLegacyIdsAsync(targetConnectionString, cancellationToken);

        var plan = LegacyAccountPlanner.Plan(
            new LegacyAccountSources(
                records,
                members,
                uidBySubject,
                identities.GetValueOrDefault(EmailProvider, []),
                identities.GetValueOrDefault(Auth0Provider, []),
                legacyIdByProfileId),
            generatedNames);

        using var client = new KeycloakAdminClient(keycloak, userPageSize, importChunkSize);
        await client.EnsureRealmPrerequisitesAsync(cancellationToken);

        var before = await client.LoadUsersAsync(cancellationToken);
        var userIdByAccount = new Dictionary<PlannedAccount, string>(ReferenceEqualityComparer.Instance);
        var missing = new List<PlannedAccount>();
        var toCreate = new List<PlannedAccount>();
        var generatedForTakenUsername = 0;
        var accountConflicts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ExistingUnverifiedConflict] = 0,
            [UsernameTakenConflict] = 0,
        };

        foreach (var account in plan.Accounts)
        {
            var match = before.Match(account);
            switch (match.Kind)
            {
                case AccountMatchKind.Existing:
                    userIdByAccount[account] = match.UserId!;
                    break;
                case AccountMatchKind.Missing:
                    missing.Add(account);
                    toCreate.Add(account with { Username = match.Username! });
                    if (!string.Equals(match.Username, account.Username, StringComparison.Ordinal))
                    {
                        generatedForTakenUsername++;
                    }

                    break;
                case AccountMatchKind.ExistingUnverified:
                    accountConflicts[ExistingUnverifiedConflict]++;
                    break;
                case AccountMatchKind.UsernameTaken:
                    accountConflicts[UsernameTakenConflict]++;
                    break;
            }
        }

        var existing = userIdByAccount.Count;
        var created = toCreate.Count == 0 ? 0 : await client.CreateUsersAsync(toCreate, cancellationToken);

        // Every created user is read back, so a profile link only ever points at a user that
        // exists with the account's planned id.
        var unresolved = 0;
        if (missing.Count > 0)
        {
            var after = await client.LoadUsersAsync(cancellationToken);
            foreach (var account in missing)
            {
                var match = after.Match(account);
                if (match.Kind == AccountMatchKind.Existing
                    && string.Equals(match.UserId, account.KeycloakUserId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    userIdByAccount[account] = match.UserId!;
                }
                else
                {
                    unresolved++;
                }
            }
        }

        var oidcProfileIdBySubject = identities.GetValueOrDefault(OidcProvider, []);
        var linksToInsert = new Dictionary<string, long>(StringComparer.Ordinal);
        var present = 0;
        var conflicts = 0;

        foreach (var (account, keycloakUserId) in userIdByAccount)
        {
            if (account.ProfileId is not { } profileId)
            {
                continue;
            }

            if (oidcProfileIdBySubject.TryGetValue(keycloakUserId, out var linkedProfileId))
            {
                if (linkedProfileId == profileId)
                {
                    present++;
                }
                else
                {
                    conflicts++;
                }

                continue;
            }

            linksToInsert.TryAdd(keycloakUserId, profileId);
        }

        var inserted = await InsertOidcIdentitiesAsync(targetConnectionString, linksToInsert, cancellationToken);

        return new KeycloakImportSummary(
            records.Count,
            plan.Accounts.Count,
            plan.Accounts
                .GroupBy(account => account.UsernameSource)
                .OrderBy(group => group.Key)
                .ToDictionary(group => SourceName(group.Key), group => group.Count(), StringComparer.Ordinal),
            created,
            existing,
            generatedForTakenUsername,
            new Dictionary<string, int> { [DroppedNoContactNoProfile] = plan.DroppedNoContactNoProfile },
            accountConflicts,
            inserted,
            present,
            conflicts,
            unresolved);
    }

    // Every fe_users row with its username, last login and email, and the legacy id each
    // fe_users_auth0 subject maps to.
    private static async Task<(List<LegacyMember> Members, Dictionary<string, int> UidBySubject)> LoadLegacyMembersAsync(
        string legacyConnectionString, CancellationToken cancellationToken)
    {
        var members = new List<LegacyMember>();
        var uidBySubject = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var connection = new MySqlConnection(legacyConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var command = new MySqlCommand("SELECT uid, username, lastlogin, email FROM fe_users", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                members.Add(new LegacyMember(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        await using (var command = new MySqlCommand(
            "SELECT sub, user_id FROM fe_users_auth0 WHERE user_id IS NOT NULL", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                uidBySubject[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        return (members, uidBySubject);
    }

    private static async Task<Dictionary<long, int>> LoadProfileLegacyIdsAsync(
        string targetConnectionString, CancellationToken cancellationToken)
    {
        var legacyIdByProfileId = new Dictionary<long, int>();

        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, legacy_id FROM profile WHERE legacy_id IS NOT NULL", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            legacyIdByProfileId[reader.GetInt64(0)] = reader.GetInt32(1);
        }

        return legacyIdByProfileId;
    }

    // Profile id by subject, grouped by provider, for the providers the import reads.
    private static async Task<Dictionary<string, Dictionary<string, long>>> LoadProfileIdentitiesAsync(
        string targetConnectionString, CancellationToken cancellationToken)
    {
        var byProvider = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT provider, subject, profile_id FROM profile_identity WHERE provider = ANY(@providers)",
            connection);
        command.Parameters.AddWithValue("providers", new[] { EmailProvider, Auth0Provider, OidcProvider });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var provider = reader.GetString(0);
            if (!byProvider.TryGetValue(provider, out var bySubject))
            {
                bySubject = new Dictionary<string, long>(StringComparer.Ordinal);
                byProvider[provider] = bySubject;
            }

            bySubject[reader.GetString(1)] = reader.GetInt64(2);
        }

        return byProvider;
    }

    private static string SourceName(LoginUsernameSource source) => source switch
    {
        LoginUsernameSource.Legacy => "legacy",
        LoginUsernameSource.NoLegacyRow => "no_legacy_row",
        LoginUsernameSource.EmailLike => "email_like",
        LoginUsernameSource.RejectedByKeycloak => "rejected_by_keycloak",
        LoginUsernameSource.Duplicate => "duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private static async Task<int> InsertOidcIdentitiesAsync(
        string targetConnectionString, IReadOnlyDictionary<string, long> profileIdBySubject, CancellationToken cancellationToken)
    {
        if (profileIdBySubject.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        // The unique index on (provider, subject) makes a concurrent insert of the same link a
        // no-op, so a subject linked in the meantime keeps its existing profile.
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO profile_identity (profile_id, provider, subject, created_at)
            SELECT link.profile_id, @provider, link.subject, now()
            FROM unnest(@subjects, @profile_ids) AS link(subject, profile_id)
            ON CONFLICT (provider, subject) DO NOTHING
            """,
            connection);
        command.Parameters.AddWithValue("provider", OidcProvider);
        command.Parameters.AddWithValue("subjects", profileIdBySubject.Keys.ToArray());
        command.Parameters.AddWithValue("profile_ids", profileIdBySubject.Values.ToArray());

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The outcome of one Keycloak account import: Auth0 records read, accounts planned, the planned
/// login usernames per source (legacy, no_legacy_row, email_like, rejected_by_keycloak,
/// duplicate), how many accounts Keycloak added and how many already existed, how many were
/// created with their generated username because another user holds their login username,
/// dropped records per reason, accounts skipped per conflict reason (existing_unverified,
/// username_taken), the oidc
/// profile links inserted, already present, or left untouched because their subject belongs to
/// another profile, and the accounts that could not be found in Keycloak after the import.
/// </summary>
public sealed record KeycloakImportSummary(
    int RecordsRead,
    int AccountsPlanned,
    IReadOnlyDictionary<string, int> LoginUsernamesBySource,
    int Created,
    int Existing,
    int GeneratedForTakenUsername,
    IReadOnlyDictionary<string, int> DroppedByReason,
    IReadOnlyDictionary<string, int> AccountConflictsByReason,
    int ProfileLinksInserted,
    int ProfileLinksPresent,
    int ProfileLinkConflicts,
    int Unresolved);
