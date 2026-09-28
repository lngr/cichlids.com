using System.Security.Cryptography;
using System.Text;

namespace Cichlids.Infrastructure.Identity;

/// <summary>
/// Derives names of the form user followed by eight digits from an HMAC-SHA256 keyed on the slug
/// secret. The same input under the same secret always yields the same name, so a repeated run
/// assigns every member the same name, and the name reveals nothing about its input to anyone
/// without the secret. Inputs carry a domain prefix (handle: for public handles, login: for login
/// usernames), so one member's handle and login username are unrelated values.
/// </summary>
public sealed class GeneratedNames
{
    private const string Prefix = "user";

    // Eight digits without a leading zero: 10,000,000 to 99,999,999.
    private const ulong Lowest = 10_000_000;
    private const ulong Range = 90_000_000;

    private readonly byte[] _secretBytes;

    public GeneratedNames(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// Returns the input for a public handle keyed by the given stable key.
    /// </summary>
    public static string HandleInput(string key) => $"handle:{key}";

    /// <summary>
    /// Returns the input for a login username keyed by the given stable key.
    /// </summary>
    public static string LoginInput(string key) => $"login:{key}";

    /// <summary>
    /// Returns the name for the given input.
    /// </summary>
    public string Generate(string input)
    {
        var hash = HMACSHA256.HashData(_secretBytes, Encoding.UTF8.GetBytes(input));

        ulong numeric = 0;
        for (var i = 0; i < 8; i++)
        {
            numeric = (numeric << 8) | hash[i];
        }

        return $"{Prefix}{Lowest + (numeric % Range)}";
    }

    /// <summary>
    /// Returns the endless sequence of candidate names for the given input: first the name of the
    /// input itself, then the names of the input with an increasing counter appended, so every
    /// candidate is a keyed hash of its own and the sequence is the same on every run.
    /// </summary>
    public IEnumerable<string> Candidates(string input)
    {
        for (var attempt = 0; ; attempt++)
        {
            yield return Generate(attempt == 0 ? input : $"{input}:{attempt}");
        }
    }

    /// <summary>
    /// Returns the first candidate for the given input that isTaken does not reject.
    /// </summary>
    public string GenerateUnique(string input, Func<string, bool> isTaken) =>
        Candidates(input).First(candidate => !isTaken(candidate));
}
