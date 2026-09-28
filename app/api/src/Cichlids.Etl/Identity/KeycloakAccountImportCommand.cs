using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Identity;

/// <summary>
/// The "keycloak-import" command: creates a Keycloak user for every legacy account from the
/// Auth0 export that has a known email or a migrated profile, and links each account with a
/// migrated profile to that profile through an oidc profile identity whose subject is the
/// Keycloak user id. Existing Keycloak users are never changed. An account whose email belongs to
/// an existing user with a verified email is linked to that user; an account whose email belongs
/// to an existing user with an unverified email, or whose username another user holds, is
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
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Importing legacy accounts from {auth0ExportPath} into Keycloak realm '{keycloak.Realm}' at {keycloak.BaseUrl}...");

        var summary = await ImportAsync(
            legacyConnectionString, targetConnectionString, auth0ExportPath, keycloak, cancellationToken);

        Console.WriteLine($"Auth0 records read:        {summary.RecordsRead}");
        Console.WriteLine($"Accounts planned:          {summary.AccountsPlanned}");
        Console.WriteLine($"  created:                 {summary.Created}");
        Console.WriteLine($"  existing:                {summary.Existing}");
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
    /// Plans the accounts from the Auth0 export, the legacy user table and the migrated profile
    /// identities, ensures the realm prerequisites, creates the missing Keycloak users and inserts
    /// the missing oidc profile identities.
    /// </summary>
    public static Task<KeycloakImportSummary> ImportAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string auth0ExportPath,
        KeycloakAdminSettings keycloak,
        CancellationToken cancellationToken) =>
        ImportAsync(
            legacyConnectionString,
            targetConnectionString,
            auth0ExportPath,
            keycloak,
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
        int userPageSize,
        int importChunkSize,
        CancellationToken cancellationToken)
    {
        var records = Auth0ExportReader.ReadFile(auth0ExportPath);
        var legacyEmailBySubject = await LoadLegacyEmailsAsync(legacyConnectionString, cancellationToken);
        var identities = await LoadProfileIdentitiesAsync(targetConnectionString, cancellationToken);

        var plan = LegacyAccountPlanner.Plan(
            records,
            legacyEmailBySubject,
            identities.GetValueOrDefault(EmailProvider, []),
            identities.GetValueOrDefault(Auth0Provider, []));

        using var client = new KeycloakAdminClient(keycloak, userPageSize, importChunkSize);
        await client.EnsureRealmPrerequisitesAsync(cancellationToken);

        var before = await client.LoadUsersAsync(cancellationToken);
        var userIdByAccount = new Dictionary<PlannedAccount, string>(ReferenceEqualityComparer.Instance);
        var missing = new List<PlannedAccount>();
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
        var created = missing.Count == 0 ? 0 : await client.CreateUsersAsync(missing, cancellationToken);

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
            created,
            existing,
            new Dictionary<string, int> { [DroppedNoContactNoProfile] = plan.DroppedNoContactNoProfile },
            accountConflicts,
            inserted,
            present,
            conflicts,
            unresolved);
    }

    private static async Task<Dictionary<string, string>> LoadLegacyEmailsAsync(
        string legacyConnectionString, CancellationToken cancellationToken)
    {
        var emailBySubject = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var connection = new MySqlConnection(legacyConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "SELECT a.sub, u.email FROM fe_users_auth0 a JOIN fe_users u ON u.uid = a.user_id WHERE u.email <> ''",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var email = reader.GetString(1).Trim().ToLowerInvariant();
            if (email.Length > 0)
            {
                emailBySubject[reader.GetString(0)] = email;
            }
        }

        return emailBySubject;
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
/// The outcome of one Keycloak account import: Auth0 records read, accounts planned, how many of
/// them Keycloak added and how many already existed, dropped records per reason, accounts
/// skipped per conflict reason (existing_unverified, username_taken), the oidc
/// profile links inserted, already present, or left untouched because their subject belongs to
/// another profile, and the accounts that could not be found in Keycloak after the import.
/// </summary>
public sealed record KeycloakImportSummary(
    int RecordsRead,
    int AccountsPlanned,
    int Created,
    int Existing,
    IReadOnlyDictionary<string, int> DroppedByReason,
    IReadOnlyDictionary<string, int> AccountConflictsByReason,
    int ProfileLinksInserted,
    int ProfileLinksPresent,
    int ProfileLinkConflicts,
    int Unresolved);
