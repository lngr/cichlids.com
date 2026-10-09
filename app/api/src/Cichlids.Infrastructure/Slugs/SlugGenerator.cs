using System.Security.Cryptography;
using System.Text;

namespace Cichlids.Infrastructure.Slugs;

/// <summary>
/// Derives short, lowercase base36 slugs from an HMAC-SHA256 keyed on a shared secret. The same
/// input under the same secret always resolves to the same slug (so callers can regenerate a
/// row's slug idempotently from its own stable identifier), while the mapping from input to slug
/// cannot be predicted or enumerated without knowing the secret, unlike a plain unkeyed hash.
/// </summary>
public sealed class SlugGenerator
{
    /// <summary>
    /// Configuration key the secret is bound from ("Slugs:Secret").
    /// </summary>
    public const string SecretConfigurationKey = "Slugs:Secret";

    /// <summary>
    /// Environment variable that overrides the configured secret.
    /// </summary>
    public const string SecretEnvironmentVariable = "CICHLIDS_SLUG_SECRET";

    private const int Length = 10;
    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    private static readonly ulong Modulus = Pow36(Length);

    private readonly byte[] _secretBytes;

    public SlugGenerator(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// Returns the fixed-length, lowercase base36 slug for the given input, left-padded with '0'
    /// when the reduced hash value does not fill all ten digits.
    /// </summary>
    public string Generate(string input)
    {
        var hash = HMACSHA256.HashData(_secretBytes, Encoding.UTF8.GetBytes(input));

        ulong numeric = 0;
        for (var i = 0; i < 8; i++)
        {
            numeric = (numeric << 8) | hash[i];
        }

        numeric %= Modulus;

        var chars = new char[Length];
        for (var i = Length - 1; i >= 0; i--)
        {
            chars[i] = Digits[(int)(numeric % 36)];
            numeric /= 36;
        }

        return new string(chars);
    }

    /// <summary>
    /// Returns the first candidate for the given input that isTaken does not reject, retrying by
    /// appending an increasing counter to the HMAC input (rather than to the resulting slug) so
    /// every candidate stays a full-length keyed hash of its own.
    /// </summary>
    public string GenerateUnique(string input, Func<string, bool> isTaken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var candidateInput = attempt == 0 ? input : $"{input}:{attempt}";
            var candidate = Generate(candidateInput);
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }

    private static ulong Pow36(int exponent)
    {
        var result = 1UL;
        for (var i = 0; i < exponent; i++)
        {
            result *= 36;
        }

        return result;
    }
}
