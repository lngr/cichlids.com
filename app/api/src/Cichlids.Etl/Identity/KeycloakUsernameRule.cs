using System.Text;

namespace Cichlids.Etl.Identity;

/// <summary>
/// The username rule of Keycloak 26.6.4 with its default user profile, applied before the import
/// sends a username. Keycloak lowercases a username and then requires 3 to 255 characters, none of
/// the characters its username-prohibited-characters validator excludes, and only characters of
/// the Latin and Common scripts (its IDN homograph validator). A username Keycloak rejects would
/// fail the whole partial import request it is part of.
/// </summary>
public static class KeycloakUsernameRule
{
    private const int MinLength = 3;
    private const int MaxLength = 255;

    // Java lowercases the capital I with dot above to i followed by a combining dot above; the
    // simple case mapping .NET applies drops the dot. Every other character Keycloak can accept
    // lowercases the same way on both platforms.
    private const char CapitalIWithDotAbove = (char)0x0130;
    private const string LowercaseIWithDotAbove = "i" + "\u0307";

    // Keycloak's prohibited characters pattern is ^[^<>&"'\s\v\h$%!#?S,;:*~/\\|^=\[\]{}()`\p{Cntrl}]+$
    // with S standing for the section sign U+00A7. These are its symbols; IsProhibited checks its
    // whitespace and control classes by code point.
    private const string ProhibitedSymbols = "<>&\"'$%!#?\u00a7,;:*~/\\|^=[]{}()`";

    // Java's String.trim removes every character up to and including the space.
    private static readonly char[] JavaTrimmedCharacters = Enumerable.Range(0, 0x21).Select(i => (char)i).ToArray();

    // The code point ranges of the Latin and Common scripts, merged, as Java 21 (the runtime of
    // Keycloak 26.6.4, Unicode 15.0) assigns them. Pairs of first and last code point.
    private static readonly int[] LatinOrCommonRanges =
    [
        0x0000, 0x02E9, 0x02EC, 0x02FF, 0x0374, 0x0374, 0x037E, 0x037E,
        0x0385, 0x0385, 0x0387, 0x0387, 0x0605, 0x0605, 0x060C, 0x060C,
        0x061B, 0x061B, 0x061F, 0x061F, 0x0640, 0x0640, 0x06DD, 0x06DD,
        0x08E2, 0x08E2, 0x0964, 0x0965, 0x0E3F, 0x0E3F, 0x0FD5, 0x0FD8,
        0x10FB, 0x10FB, 0x16EB, 0x16ED, 0x1735, 0x1736, 0x1802, 0x1803,
        0x1805, 0x1805, 0x1CD3, 0x1CD3, 0x1CE1, 0x1CE1, 0x1CE9, 0x1CEC,
        0x1CEE, 0x1CF3, 0x1CF5, 0x1CF7, 0x1CFA, 0x1CFA, 0x1D00, 0x1D25,
        0x1D2C, 0x1D5C, 0x1D62, 0x1D65, 0x1D6B, 0x1D77, 0x1D79, 0x1DBE,
        0x1E00, 0x1EFF, 0x2000, 0x200B, 0x200E, 0x2064, 0x2066, 0x2071,
        0x2074, 0x208E, 0x2090, 0x209C, 0x20A0, 0x20C0, 0x2100, 0x2125,
        0x2127, 0x218B, 0x2190, 0x2426, 0x2440, 0x244A, 0x2460, 0x27FF,
        0x2900, 0x2B73, 0x2B76, 0x2B95, 0x2B97, 0x2BFF, 0x2C60, 0x2C7F,
        0x2E00, 0x2E5D, 0x2FF0, 0x2FFB, 0x3000, 0x3004, 0x3006, 0x3006,
        0x3008, 0x3020, 0x3030, 0x3037, 0x303C, 0x303F, 0x309B, 0x309C,
        0x30A0, 0x30A0, 0x30FB, 0x30FC, 0x3190, 0x319F, 0x31C0, 0x31E3,
        0x3220, 0x325F, 0x327F, 0x32CF, 0x32FF, 0x32FF, 0x3358, 0x33FF,
        0x4DC0, 0x4DFF, 0xA700, 0xA7CA, 0xA7D0, 0xA7D1, 0xA7D3, 0xA7D3,
        0xA7D5, 0xA7D9, 0xA7F2, 0xA7FF, 0xA830, 0xA839, 0xA92E, 0xA92E,
        0xA9CF, 0xA9CF, 0xAB30, 0xAB64, 0xAB66, 0xAB6B, 0xFB00, 0xFB06,
        0xFD3E, 0xFD3F, 0xFE10, 0xFE19, 0xFE30, 0xFE52, 0xFE54, 0xFE66,
        0xFE68, 0xFE6B, 0xFEFF, 0xFEFF, 0xFF01, 0xFF65, 0xFF70, 0xFF70,
        0xFF9E, 0xFF9F, 0xFFE0, 0xFFE6, 0xFFE8, 0xFFEE, 0xFFF9, 0xFFFD,
        0x10100, 0x10102, 0x10107, 0x10133, 0x10137, 0x1013F, 0x10190, 0x1019C,
        0x101D0, 0x101FC, 0x102E1, 0x102FB, 0x10780, 0x10785, 0x10787, 0x107B0,
        0x107B2, 0x107BA, 0x1BCA0, 0x1BCA3, 0x1CF50, 0x1CFC3, 0x1D000, 0x1D0F5,
        0x1D100, 0x1D126, 0x1D129, 0x1D166, 0x1D16A, 0x1D17A, 0x1D183, 0x1D184,
        0x1D18C, 0x1D1A9, 0x1D1AE, 0x1D1EA, 0x1D2C0, 0x1D2D3, 0x1D2E0, 0x1D2F3,
        0x1D300, 0x1D356, 0x1D360, 0x1D378, 0x1D400, 0x1D454, 0x1D456, 0x1D49C,
        0x1D49E, 0x1D49F, 0x1D4A2, 0x1D4A2, 0x1D4A5, 0x1D4A6, 0x1D4A9, 0x1D4AC,
        0x1D4AE, 0x1D4B9, 0x1D4BB, 0x1D4BB, 0x1D4BD, 0x1D4C3, 0x1D4C5, 0x1D505,
        0x1D507, 0x1D50A, 0x1D50D, 0x1D514, 0x1D516, 0x1D51C, 0x1D51E, 0x1D539,
        0x1D53B, 0x1D53E, 0x1D540, 0x1D544, 0x1D546, 0x1D546, 0x1D54A, 0x1D550,
        0x1D552, 0x1D6A5, 0x1D6A8, 0x1D7CB, 0x1D7CE, 0x1D7FF, 0x1DF00, 0x1DF1E,
        0x1DF25, 0x1DF2A, 0x1EC71, 0x1ECB4, 0x1ED01, 0x1ED3D, 0x1F000, 0x1F02B,
        0x1F030, 0x1F093, 0x1F0A0, 0x1F0AE, 0x1F0B1, 0x1F0BF, 0x1F0C1, 0x1F0CF,
        0x1F0D1, 0x1F0F5, 0x1F100, 0x1F1AD, 0x1F1E6, 0x1F1FF, 0x1F201, 0x1F202,
        0x1F210, 0x1F23B, 0x1F240, 0x1F248, 0x1F250, 0x1F251, 0x1F260, 0x1F265,
        0x1F300, 0x1F6D7, 0x1F6DC, 0x1F6EC, 0x1F6F0, 0x1F6FC, 0x1F700, 0x1F776,
        0x1F77B, 0x1F7D9, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0, 0x1F800, 0x1F80B,
        0x1F810, 0x1F847, 0x1F850, 0x1F859, 0x1F860, 0x1F887, 0x1F890, 0x1F8AD,
        0x1F8B0, 0x1F8B1, 0x1F900, 0x1FA53, 0x1FA60, 0x1FA6D, 0x1FA70, 0x1FA7C,
        0x1FA80, 0x1FA88, 0x1FA90, 0x1FABD, 0x1FABF, 0x1FAC5, 0x1FACE, 0x1FADB,
        0x1FAE0, 0x1FAE8, 0x1FAF0, 0x1FAF8, 0x1FB00, 0x1FB92, 0x1FB94, 0x1FBCA,
        0x1FBF0, 0x1FBF9, 0xE0001, 0xE0001, 0xE0020, 0xE007F,
    ];

    /// <summary>
    /// Returns the form Keycloak stores a username in: lowercased the way Java lowercases it.
    /// </summary>
    public static string Normalize(string username) =>
        username.Replace(CapitalIWithDotAbove.ToString(), LowercaseIWithDotAbove, StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// Returns true when Keycloak accepts the username.
    /// </summary>
    public static bool IsAccepted(string username)
    {
        var normalized = Normalize(username);

        // Keycloak's length validator trims the way Java's String.trim does and counts UTF-16
        // code units.
        var length = normalized.AsSpan().Trim(JavaTrimmedCharacters).Length;
        if (length < MinLength || length > MaxLength)
        {
            return false;
        }

        var remaining = normalized.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != System.Buffers.OperationStatus.Done
                || IsProhibited(rune.Value)
                || !IsLatinOrCommon(rune.Value))
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }

    // Java's \s, \v and \h classes and the POSIX control class, plus the listed symbols.
    private static bool IsProhibited(int codePoint) =>
        codePoint <= 0x20
        || codePoint == 0x7F
        || codePoint == 0x85
        || codePoint == 0xA0
        || codePoint == 0x1680
        || codePoint == 0x180E
        || codePoint is >= 0x2000 and <= 0x200A
        || codePoint == 0x2028
        || codePoint == 0x2029
        || codePoint == 0x202F
        || codePoint == 0x205F
        || codePoint == 0x3000
        || (codePoint <= char.MaxValue && ProhibitedSymbols.Contains((char)codePoint, StringComparison.Ordinal));

    private static bool IsLatinOrCommon(int codePoint)
    {
        int low = 0, high = (LatinOrCommonRanges.Length / 2) - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < LatinOrCommonRanges[2 * middle])
            {
                high = middle - 1;
            }
            else if (codePoint > LatinOrCommonRanges[(2 * middle) + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
