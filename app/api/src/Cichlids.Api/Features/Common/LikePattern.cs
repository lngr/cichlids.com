namespace Cichlids.Api.Features.Common;

/// <summary>
/// Builds patterns for the PostgreSQL ILIKE operator from user input. The wildcard characters
/// percent and underscore and the escape character itself are escaped, so the input matches only
/// its literal characters.
/// </summary>
public static class LikePattern
{
    /// <summary>
    /// Returns the value with every LIKE wildcard and the backslash escape character escaped.
    /// </summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// Returns a pattern matching every text that contains the value literally.
    /// </summary>
    public static string Contains(string value) => $"%{Escape(value)}%";
}
