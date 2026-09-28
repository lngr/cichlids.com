using System.Security.Cryptography;
using System.Text;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Computes RFC 4122 version 5 UUIDs (name-based, SHA-1) so that the same namespace and name
/// always produce the same id without a lookup.
/// </summary>
public static class Uuidv5
{
    /// <summary>
    /// Computes the version 5 UUID for the given namespace and name.
    /// </summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = ToRfcByteOrder(namespaceId.ToByteArray());
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);
        var uuidBytes = new byte[16];
        Array.Copy(hash, uuidBytes, uuidBytes.Length);

        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x50);
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

        return new Guid(ToRfcByteOrder(uuidBytes));
    }

    // A Guid's byte array holds its first three fields (time_low, time_mid,
    // time_hi_and_version) in little-endian order, while RFC 4122 defines and prints those
    // fields big-endian. Swapping each field's bytes converts one representation into the
    // other, and the swap is its own inverse, so this same helper is used in both directions.
    private static byte[] ToRfcByteOrder(byte[] guidBytes) =>
    [
        guidBytes[3], guidBytes[2], guidBytes[1], guidBytes[0],
        guidBytes[5], guidBytes[4],
        guidBytes[7], guidBytes[6],
        guidBytes[8], guidBytes[9], guidBytes[10], guidBytes[11],
        guidBytes[12], guidBytes[13], guidBytes[14], guidBytes[15],
    ];
}
