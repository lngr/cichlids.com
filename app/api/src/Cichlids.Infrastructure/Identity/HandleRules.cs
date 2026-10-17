using System.Text.RegularExpressions;

namespace Cichlids.Infrastructure.Identity;

/// <summary>
/// The verdict on a handle a member asks for.
/// </summary>
public enum HandleCheck
{
    Valid,
    Invalid,
    Generated,
    Reserved,
}

/// <summary>
/// The verdict together with the normalized handle: surrounding whitespace removed and lowercased.
/// </summary>
public readonly record struct HandleVerdict(HandleCheck Check, string Handle);

/// <summary>
/// The rules a public handle chosen by a member has to meet: 3 to 30 characters, lowercase letters
/// a to z, digits, underscore and dot, a letter at the start, a letter or digit at the end and no
/// two dots in a row. It is neither of the form of a generated name (user followed by eight
/// digits, guest_ followed by a number, a provisional- prefix) nor a reserved name. The input is
/// lowercased before the check.
/// </summary>
public static partial class HandleRules
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "cichlids", "support", "help", "moderator", "system", "api", "www", "root", "official",
    };

    /// <summary>
    /// Normalizes the input and classifies it. Generated forms and reserved names are reported
    /// before malformed input, so a name like provisional-1 is recognised as a generated form.
    /// </summary>
    public static HandleVerdict Check(string? input)
    {
        var handle = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (GeneratedForm().IsMatch(handle))
        {
            return new HandleVerdict(HandleCheck.Generated, handle);
        }

        if (ReservedNames.Contains(handle) || ReservedUsernames.IsReserved(handle))
        {
            return new HandleVerdict(HandleCheck.Reserved, handle);
        }

        var allowed = AllowedForm().IsMatch(handle) && !handle.Contains("..", StringComparison.Ordinal);
        return new HandleVerdict(allowed ? HandleCheck.Valid : HandleCheck.Invalid, handle);
    }

    [GeneratedRegex("^(user[0-9]{8}|guest_[0-9]+|provisional-.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedForm();

    [GeneratedRegex("^[a-z][a-z0-9_.]{1,28}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedForm();
}
