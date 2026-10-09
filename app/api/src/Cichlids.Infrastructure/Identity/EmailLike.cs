using System.Text.RegularExpressions;

namespace Cichlids.Infrastructure.Identity;

/// <summary>
/// Recognises values that contain an email address: a local part, an at sign and a domain with
/// at least one dot, none of them containing whitespace. An at sign without such a domain, as in
/// a nickname like DM@Montreal, reveals no address and does not count.
/// </summary>
public static partial class EmailLike
{
    /// <summary>
    /// Returns true when the value contains something shaped like an email address anywhere in
    /// it, and false for null or blank values.
    /// </summary>
    public static bool Contains(string? value) =>
        !string.IsNullOrWhiteSpace(value) && AddressPattern().IsMatch(value);

    [GeneratedRegex(@"[^\s@]+@[^\s@.]+(\.[^\s@.]+)+")]
    private static partial Regex AddressPattern();
}
