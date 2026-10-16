using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// The receiving side of a push subscription: a browser with its P-256 key pair and its
/// authentication secret. It builds the subscription a web app posts and decrypts what an
/// application server sends it (RFC 8291 over RFC 8188, content coding aes128gcm).
/// </summary>
internal sealed class FakeBrowser : IDisposable
{
    private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _authSecret = RandomNumberGenerator.GetBytes(16);

    public FakeBrowser(string endpoint) => Endpoint = endpoint;

    public string Endpoint { get; }

    public static string NewEndpoint(string host = "push.example.test") => $"https://{host}/send/{Guid.NewGuid():N}";

    public byte[] PublicKey
    {
        get
        {
            var q = _key.ExportParameters(false).Q;
            return [0x04, .. q.X!, .. q.Y!];
        }
    }

    public string P256dh => WebEncoders.Base64UrlEncode(PublicKey);

    public string Auth => WebEncoders.Base64UrlEncode(_authSecret);

    /// <summary>The body a web app posts to subscribe, as the browser's subscription JSON plus the time zone.</summary>
    public object SubscribeBody(string timeZone = "Europe/Berlin") =>
        new { endpoint = Endpoint, keys = new { p256dh = P256dh, auth = Auth }, timeZone };

    public string Decrypt(byte[] message)
    {
        var salt = message[..16];
        var recordSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(16, 4));
        var keyIdLength = message[20];
        var serverPublic = message[21..(21 + keyIdLength)];
        var ciphertext = message[(21 + keyIdLength)..];
        Assert.True(recordSize >= ciphertext.Length, "The message fits one record.");

        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] },
        });
        var secret = _key.DeriveRawSecretAgreement(server.PublicKey);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, secret, _authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), PublicKey, serverPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var plaintext = new byte[ciphertext.Length - 16];
        using var aes = new AesGcm(contentKey, 16);
        aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plaintext);
        var end = plaintext.Length;
        while (end > 0 && plaintext[end - 1] == 0) end--;
        Assert.Equal(0x02, plaintext[end - 1]);
        return Encoding.UTF8.GetString(plaintext, 0, end - 1);
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    public void Dispose() => _key.Dispose();
}
