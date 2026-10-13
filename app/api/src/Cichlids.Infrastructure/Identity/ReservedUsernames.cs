using System.Text.RegularExpressions;

namespace Cichlids.Infrastructure.Identity;

/// <summary>
/// The usernames of the system and placeholder profiles: the community archive, the former
/// members and the forum members. New accounts cannot take them, so nobody appears as one of these
/// profiles. The comparison ignores case and surrounding whitespace.
/// </summary>
public static partial class ReservedUsernames
{
    /// <summary>
    /// The username of the profile that owns anonymous legacy content.
    /// </summary>
    public const string CommunityArchive = "community-archive";

    private const string FormerMemberPrefix = "former-member-";
    private const string ForumMemberPrefix = "forum-member-";

    /// <summary>
    /// Returns the username of the placeholder profile for a legacy user without a member profile.
    /// </summary>
    public static string FormerMember(int legacyUserId) => $"{FormerMemberPrefix}{legacyUserId}";

    /// <summary>
    /// Returns the username of the placeholder profile for a forum author without a member profile.
    /// </summary>
    public static string ForumMember(int phorumUserId) => $"{ForumMemberPrefix}{phorumUserId}";

    /// <summary>
    /// Returns true when the value is the name of a system or placeholder profile.
    /// </summary>
    public static bool IsReserved(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var name = value.Trim();
        return name.Equals(CommunityArchive, StringComparison.OrdinalIgnoreCase)
            || PlaceholderName().IsMatch(name);
    }

    [GeneratedRegex("^(former|forum)-member-[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderName();
}
