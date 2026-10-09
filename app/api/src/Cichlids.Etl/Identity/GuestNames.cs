using System.Globalization;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Identity;
using MySqlConnector;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Public names for guest posters whose legacy name contains an email address. Every distinct such
/// name, compared trimmed and lowercased, gets guest_ followed by a number. The numbers count from
/// 1 in the order of a keyed hash of the name, so the same source names and secret always yield the
/// same assignment, and a number reveals nothing about the address behind it. Numbers have five
/// digits with leading zeros, and as many digits as the count of names needs beyond 99,999. A name
/// without an address passes through unchanged.
/// </summary>
public sealed class GuestNames
{
    private const string Prefix = "guest_";
    private const int MinimumDigits = 5;

    private readonly Dictionary<string, string> _nameByKey;

    private GuestNames(Dictionary<string, string> nameByKey)
    {
        _nameByKey = nameByKey;
    }

    /// <summary>
    /// The number of distinct guest names that contain an email address.
    /// </summary>
    public int Count => _nameByKey.Count;

    /// <summary>
    /// Builds the assignment for the given source guest names. Names without an address and
    /// repeated names are ignored.
    /// </summary>
    public static GuestNames Create(IEnumerable<string?> posterNames, GeneratedNames generatedNames)
    {
        var orderedKeys = posterNames
            .Where(EmailLike.Contains)
            .Select(name => Key(name!))
            .Distinct(StringComparer.Ordinal)
            .Select(key => (Key: key, Hash: generatedNames.Hash(GeneratedNames.GuestInput(key))))
            .OrderBy(entry => entry.Hash, HashComparer.Instance)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Key)
            .ToList();

        var digits = Math.Max(MinimumDigits, orderedKeys.Count.ToString(CultureInfo.InvariantCulture).Length);

        var nameByKey = new Dictionary<string, string>(orderedKeys.Count, StringComparer.Ordinal);
        for (var i = 0; i < orderedKeys.Count; i++)
        {
            nameByKey[orderedKeys[i]] = Prefix + (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        return new GuestNames(nameByKey);
    }

    /// <summary>
    /// Reads every guest name the comment and forum steps migrate from the legacy source: guest
    /// comments that are not deleted, and guest messages that are visible, not a moved notice and
    /// in a migrated forum. Builds the assignment from all of them together, so one address gets
    /// the same guest name in comments and in forum posts.
    /// </summary>
    public static async Task<GuestNames> LoadAsync(
        MySqlConnection legacy, GeneratedNames generatedNames, CancellationToken cancellationToken)
    {
        var forumIds = string.Join(", ", ForumMigrationStep.MigratedForumIds.Order());

        // Only a value with an at sign can contain an address, so the queries read no other rows.
        var queries = new[]
        {
            "SELECT poster FROM user_cichlids_comments WHERE fe_user = 0 AND deleted = 0 AND poster LIKE '%@%'",
            $"""
            SELECT author FROM {ForumMigrationStep.PhorumDatabase}.phorum_messages
            WHERE user_id = 0 AND status = 2 AND moved = 0 AND forum_id IN ({forumIds}) AND author LIKE '%@%'
            """,
        };

        var posterNames = new List<string>();
        foreach (var sql in queries)
        {
            await using var command = new MySqlCommand(sql, legacy);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    posterNames.Add(reader.GetString(0));
                }
            }
        }

        return Create(posterNames, generatedNames);
    }

    /// <summary>
    /// Returns the public name for a guest's legacy name: the generated guest name when it
    /// contains an email address, otherwise the name itself. Throws for an address outside the
    /// source names the assignment was built from, so an address never reaches a public column.
    /// </summary>
    public string? Resolve(string? posterName)
    {
        if (!EmailLike.Contains(posterName))
        {
            return posterName;
        }

        return _nameByKey.TryGetValue(Key(posterName!), out var name)
            ? name
            : throw new InvalidOperationException(
                "A guest name contains an email address that is missing from the collected source guest names.");
    }

    private static string Key(string posterName) => posterName.Trim().ToLowerInvariant();

    private sealed class HashComparer : IComparer<byte[]>
    {
        public static readonly HashComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
