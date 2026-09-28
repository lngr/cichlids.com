using System.Globalization;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Migrates legacy members with actual content (pictures, tanks or comments) into <c>profile</c>
/// and their login identities into <c>profile_identity</c>. Members without content stay in the
/// legacy database only; they never had anything for the new application to show. The public
/// handle and the display name never contain an email address: a member whose legacy username is
/// empty or holds an address gets a generated handle, and the display name falls back past any
/// candidate holding an address to the handle.
/// </summary>
public sealed class ProfileMigrationStep : IEtlStep
{
    // 2003-01-01 UTC: both legacy timestamps a profile's creation time could come from are zero
    // (a small number of very old or admin-created rows), so there is no real creation moment to
    // convert. This fixed marker is deliberately earlier than the site's actual launch, so it
    // reads as "unknown" rather than as a plausible date.
    private static readonly DateTimeOffset UnknownCreatedAtMarker = new(2003, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ValueConverter<ProfileKind, string> KindConverter = new SnakeCaseEnumConverter<ProfileKind>();

    public string Name => "profiles";

    public int Order => 20;

    public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
    {
        var stats = context.Statistics.ForStep(Name);

        var rows = await LoadEligibleUsersAsync(context, cancellationToken);
        var otherUsernames = await LoadOtherProfileUsernamesAsync(context, rows, cancellationToken);
        var usernames = ResolveUsernames(rows, otherUsernames, context.GeneratedNames, stats);
        var auth0SubjectsByUserId = await LoadAuth0SubjectsAsync(context, cancellationToken);
        var seenIdentities = new HashSet<(string Provider, string Subject)>();

        foreach (var row in rows)
        {
            stats.AddRead();

            var username = usernames[row.Uid];
            var displayName = FirstUsableDisplayName(row.Name, $"{row.FirstName} {row.LastName}".Trim(), row.Username, username);
            var createdAt = row.Crdate != 0
                ? DateTimeOffset.FromUnixTimeSeconds(row.Crdate)
                : row.Tstamp != 0
                    ? DateTimeOffset.FromUnixTimeSeconds(row.Tstamp)
                    : UnknownCreatedAtMarker;
            var lastLoginAt = row.LastLogin != 0 ? DateTimeOffset.FromUnixTimeSeconds(row.LastLogin) : (DateTimeOffset?)null;

            var values = new (string, object?)[]
            {
                ("legacy_id", row.Uid),
                ("username", username),
                ("display_name", displayName),
                ("city", NullIfEmpty(row.City)),
                ("country_code", NullIfEmpty(row.CountryCode)),
                ("external_avatar_url", NullIfEmpty(row.Auth0Image)),
                ("kind", KindConverter.ConvertToProvider(ProfileKind.Member)),
                ("created_at", createdAt),
                ("last_login_at", lastLoginAt),
            };

            var (profileId, inserted) = await PgUpsert.UpsertAsync(
                context.Target, context.Transaction, "profile", "legacy_id", values, cancellationToken);

            if (inserted)
            {
                stats.AddInserted();
            }
            else
            {
                stats.AddUpdated();
            }

            var identityRows = BuildIdentityRows(row, auth0SubjectsByUserId, seenIdentities, stats);
            await PgChildRows.ReplaceAsync(
                context.Target,
                context.Transaction,
                "profile_identity",
                "profile_id",
                profileId,
                ["provider", "subject", "created_at"],
                identityRows,
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolves every row's handle. A usable legacy username is kept, suffixed with the legacy id
    /// when an earlier row has the same one. A row without a usable legacy username gets a
    /// generated handle keyed by its legacy id, unique against every handle of this run and the
    /// usernames of profiles outside it.
    /// </summary>
    private static Dictionary<int, string> ResolveUsernames(
        IReadOnlyList<LegacyUserRow> rows, IReadOnlySet<string> otherUsernames, GeneratedNames generatedNames, StepStatistics stats)
    {
        var usable = rows.Where(r => PublicHandle.IsUsable(r.Username)).ToList();
        var counts = usable
            .GroupBy(r => r.Username, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<int, string>();
        foreach (var row in usable)
        {
            if (counts[row.Username] > 1 && !seen.Add(row.Username))
            {
                var suffixed = $"{row.Username}-{row.Uid}";
                result[row.Uid] = suffixed;
                stats.AddWarning($"username '{row.Username}' collides for legacy id {row.Uid}; suffixed to '{suffixed}'.");
            }
            else
            {
                seen.Add(row.Username);
                result[row.Uid] = row.Username;
            }
        }

        var taken = new HashSet<string>(result.Values, StringComparer.Ordinal);
        taken.UnionWith(otherUsernames);
        foreach (var row in rows.Where(r => !PublicHandle.IsUsable(r.Username)))
        {
            var generated = generatedNames.GenerateUnique(
                GeneratedNames.HandleInput(row.Uid.ToString(CultureInfo.InvariantCulture)), taken.Contains);
            taken.Add(generated);
            result[row.Uid] = generated;
        }

        return result;
    }

    // The usernames of every profile this run does not write: profiles the API created on a
    // first login, placeholder profiles and migrated profiles of members no longer eligible.
    private static async Task<HashSet<string>> LoadOtherProfileUsernamesAsync(
        EtlContext context, IReadOnlyList<LegacyUserRow> rows, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT username FROM profile WHERE legacy_id IS NULL OR NOT (legacy_id = ANY(@legacy_ids))",
            context.Target,
            context.Transaction);
        command.Parameters.AddWithValue("legacy_ids", rows.Select(r => r.Uid).ToArray());

        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static List<IReadOnlyList<object?>> BuildIdentityRows(
        LegacyUserRow row,
        IReadOnlyDictionary<int, List<string>> auth0SubjectsByUserId,
        HashSet<(string Provider, string Subject)> seenIdentities,
        StepStatistics stats)
    {
        var candidates = new List<(string Provider, string Subject)>();

        if (auth0SubjectsByUserId.TryGetValue(row.Uid, out var subs))
        {
            candidates.AddRange(subs.Select(sub => ("auth0", sub)));
        }

        var openId = NullIfEmpty(row.LegacyOpenId);
        if (openId is not null)
        {
            candidates.Add(("legacy-openid", openId));
        }

        var email = NullIfEmpty(row.Email);
        if (email is not null)
        {
            candidates.Add(("email", email.ToLowerInvariant()));
        }

        var result = new List<IReadOnlyList<object?>>();
        foreach (var (provider, subject) in candidates)
        {
            if (!seenIdentities.Add((provider, subject)))
            {
                stats.AddSkip("profile_identity_duplicate");
                continue;
            }

            result.Add(new object?[] { provider, subject, DateTimeOffset.UtcNow });
        }

        return result;
    }

    private static async Task<List<LegacyUserRow>> LoadEligibleUsersAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        // The owner id sets come from three separate single-table scans and the membership
        // filter runs in memory. An IN (SELECT ... UNION ...) subquery over the same tables
        // makes MySQL 5.7 re-evaluate the union per outer row, which does not finish in useful
        // time because the pictures and comments tables have no index on fe_user.
        var contentOwners = new HashSet<int>();
        foreach (var table in (string[])["user_cichlids_pictures", "user_cichlids_tanks", "user_cichlids_comments"])
        {
            await using var ownerCommand = new MySqlCommand(
                $"SELECT DISTINCT fe_user FROM {table}", context.Legacy);
            await using var ownerReader = await ownerCommand.ExecuteReaderAsync(cancellationToken);
            while (await ownerReader.ReadAsync(cancellationToken))
            {
                contentOwners.Add(ownerReader.GetInt32(0));
            }
        }

        const string sql = """
            SELECT
                u.uid, u.username, u.name, u.first_name, u.last_name, u.city,
                u.static_info_country, u.user_cichlids_auth0_image, u.crdate, u.tstamp,
                u.lastlogin, u.tx_dixeasylogin_openid, u.email
            FROM fe_users u
            WHERE u.deleted = 0 AND u.disable = 0
            ORDER BY u.uid
            """;

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<LegacyUserRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!contentOwners.Contains(reader.GetInt32("uid")))
            {
                continue;
            }

            result.Add(new LegacyUserRow(
                reader.GetInt32("uid"),
                reader.GetStringOrEmpty("username"),
                reader.GetStringOrEmpty("name"),
                reader.GetStringOrEmpty("first_name"),
                reader.GetStringOrEmpty("last_name"),
                reader.GetTrimmedOrNull("city"),
                reader.GetTrimmedOrNull("static_info_country"),
                reader.GetTrimmedOrNull("user_cichlids_auth0_image"),
                reader.GetInt64("crdate"),
                reader.GetInt64("tstamp"),
                reader.GetInt64("lastlogin"),
                reader.GetTrimmedOrNull("tx_dixeasylogin_openid"),
                reader.GetTrimmedOrNull("email")));
        }

        return result;
    }

    private static async Task<Dictionary<int, List<string>>> LoadAuth0SubjectsAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        const string sql = "SELECT user_id, sub FROM fe_users_auth0 WHERE user_id IS NOT NULL ORDER BY user_id, sub";

        await using var command = new MySqlCommand(sql, context.Legacy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, List<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = reader.GetInt32("user_id");
            var sub = reader.GetString("sub");

            if (!result.TryGetValue(userId, out var subs))
            {
                subs = [];
                result[userId] = subs;
            }

            subs.Add(sub);
        }

        return result;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    // The last candidate is the handle, which is always usable.
    private static string FirstUsableDisplayName(params string[] candidates) =>
        candidates.First(PublicHandle.IsUsable);

    private sealed record LegacyUserRow(
        int Uid,
        string Username,
        string Name,
        string FirstName,
        string LastName,
        string? City,
        string? CountryCode,
        string? Auth0Image,
        long Crdate,
        long Tstamp,
        long LastLogin,
        string? LegacyOpenId,
        string? Email);
}
