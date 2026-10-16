using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// The VAPID key pair of RFC 8292: a P-256 key whose public part is the 65 byte uncompressed point
/// and whose private part is the 32 byte scalar, both written as base64url without padding.
/// </summary>
public static class VapidKeys
{
    private const int PublicKeyLength = 65;
    private const int PrivateKeyLength = 32;
    private const int CoordinateLength = 32;

    /// <summary>Generates a new key pair.</summary>
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        var publicKey = new byte[PublicKeyLength];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 1 + CoordinateLength);
        return (WebEncoders.Base64UrlEncode(publicKey), WebEncoders.Base64UrlEncode(parameters.D!));
    }

    /// <summary>Whether the private key is a valid P-256 scalar whose public key is the given one.</summary>
    public static bool IsPair(string publicKey, string privateKey)
    {
        if (!TryDecode(publicKey, PublicKeyLength, out var publicBytes) || publicBytes[0] != 0x04
            || !TryDecode(privateKey, PrivateKeyLength, out var privateBytes))
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = privateBytes });
            var derived = key.ExportParameters(includePrivateParameters: false).Q;
            return derived.X!.AsSpan().SequenceEqual(publicBytes.AsSpan(1, CoordinateLength))
                && derived.Y!.AsSpan().SequenceEqual(publicBytes.AsSpan(1 + CoordinateLength, CoordinateLength));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDecode(string value, int length, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
            return bytes.Length == length;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
