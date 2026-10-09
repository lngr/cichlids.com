using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Turns a legacy picture path (<c>user_cichlids_pictures.image</c>, e.g.
/// <c>user_pics/5229/01_HPIM3766.JPG</c>) into a deterministic object storage key under
/// <c>originals/</c>. The legacy path is untrusted free text accumulated over a decade of
/// uploads and admin tooling: it can carry percent-encoded and doubly percent-encoded sequences,
/// non-ASCII characters, and characters no storage provider key should contain.
/// </summary>
public static partial class StorageKeyNormalizer
{
    [GeneratedRegex("(?:%[0-9A-Fa-f]{2})+")]
    private static partial Regex PercentEncodedRun();

    [GeneratedRegex("%[0-9A-Fa-f]{2}")]
    private static partial Regex PercentEncodedSequence();

    [GeneratedRegex("[^A-Za-z0-9._/-]")]
    private static partial Regex DisallowedCharacter();

    /// <summary>
    /// Builds the storage key for a legacy path. Paths with no directory component (a bare
    /// filename, no <c>/</c> anywhere) are filed under a dedicated unresolved bucket instead of
    /// directly under <c>originals/</c>, since their original upload location cannot be recovered.
    /// </summary>
    public static string ToStorageKey(string legacyImagePath)
    {
        var normalized = NormalizeFilename(legacyImagePath);
        return legacyImagePath.Contains('/')
            ? $"originals/{normalized}"
            : $"originals/user_pics/unresolved/{normalized}";
    }

    /// <summary>
    /// Applies the same percent-decoding, ASCII folding and disallowed-character substitution
    /// <see cref="ToStorageKey"/> uses, without its "originals/" placement decision, for callers
    /// that need a safe storage key segment under a different prefix (for example forum
    /// attachments).
    /// </summary>
    public static string NormalizeFilename(string filename) => Normalize(filename);

    /// <summary>
    /// The basename (last path segment) of the raw, un-normalized legacy path, used as
    /// <c>media_item.original_filename</c>.
    /// </summary>
    public static string GetOriginalFilename(string legacyImagePath)
    {
        var lastSlash = legacyImagePath.LastIndexOf('/');
        return lastSlash < 0 ? legacyImagePath : legacyImagePath[(lastSlash + 1)..];
    }

    private static string Normalize(string path)
    {
        var decoded = PercentDecode(path);
        var folded = FoldToAscii(decoded);
        return DisallowedCharacter().Replace(folded, "_");
    }

    // Percent-decodes once, then decodes a second time only if the first pass still leaves a
    // percent-encoded-looking sequence behind -- the signature of a legacy value that was encoded
    // twice (a literal "%" character got escaped to "%25" before the rest of the path was
    // encoded, e.g. "%252324" decodes once to "%2324" and needs one more pass to reach "#24").
    private static string PercentDecode(string path)
    {
        var once = DecodeOnce(path);
        return PercentEncodedSequence().IsMatch(once) ? DecodeOnce(once) : once;
    }

    // A run of consecutive %XX triplets is decoded as one UTF-8 byte sequence rather than one
    // character at a time, so a multi-byte UTF-8 codepoint that got percent-encoded (the common
    // case for non-ASCII filenames) reassembles into the correct character instead of mojibake.
    private static string DecodeOnce(string path)
    {
        return PercentEncodedRun().Replace(path, m =>
        {
            var bytes = new byte[m.Value.Length / 3];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(
                    m.Value.AsSpan(i * 3 + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return Encoding.UTF8.GetString(bytes);
        });
    }

    // Best-effort ASCII transliteration via Unicode decomposition (an accented Latin letter
    // decomposes into its base letter plus a separate combining mark, which is then dropped);
    // anything left over that still is not ASCII has no reasonable transliteration and becomes
    // an underscore instead of being silently dropped or crashing the run.
    private static string FoldToAscii(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c <= 0x7F ? c : '_');
        }

        return builder.ToString();
    }
}
