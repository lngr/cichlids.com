using System.Security.Cryptography;
using System.Text;
using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The guest names the shared legacy seed yields under the context's default secret. The seed
/// holds exactly two addresses among its guest names: comment uid 5014 and forum message 90040
/// share one in different letter case, and comment uid 5015 holds the other. They get the numbers
/// 1 and 2 in the order of their HMAC-SHA256 keyed hash, computed here independently of the code
/// under test.
/// </summary>
internal static class SeedGuestNames
{
    private const string SharedAddressKey = "shared.guest@example.com";
    private const string SecondAddressKey = "mail me: other.guest@example.org";

    private static readonly bool SharedComesFirst =
        string.CompareOrdinal(KeyedHashHex(SharedAddressKey), KeyedHashHex(SecondAddressKey)) < 0;

    /// <summary>
    /// The guest name of comment uid 5014 and forum message 90040.
    /// </summary>
    public static string Shared => SharedComesFirst ? "guest_00001" : "guest_00002";

    /// <summary>
    /// The guest name of comment uid 5015.
    /// </summary>
    public static string Second => SharedComesFirst ? "guest_00002" : "guest_00001";

    private static string KeyedHashHex(string key) => Convert.ToHexString(
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(EtlContext.DevDefaultSlugSecret), Encoding.UTF8.GetBytes($"guest:{key}")));
}
