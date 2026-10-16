using System.Globalization;
using System.Text;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// The bytes an installation signs to prove possession of its private key: the fixed label, the
/// challenge as the API issued it (base64url), the credential id (lowercase, hyphenated) and the
/// timestamp in whole seconds since 1970-01-01T00:00:00Z, each on a line of its own and
/// encoded as UTF-8 without a final line break. The signature is ECDSA over P-256 with SHA-256 in
/// the fixed-length r and s form (64 bytes) that WebCrypto produces.
/// </summary>
public static class DeviceSignatureMessage
{
    public const string Label = "cichlids-device-auth-v1";

    public static byte[] Build(string challenge, Guid credentialId, long timestampSeconds) =>
        Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"{Label}\n{challenge}\n{credentialId:D}\n{timestampSeconds}"));
}
