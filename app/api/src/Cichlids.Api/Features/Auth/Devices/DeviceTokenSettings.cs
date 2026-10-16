using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// A signing key of the device token issuer, identified by its key id. A key with a private part
/// signs tokens and validates them; a key without one only validates tokens.
/// </summary>
public sealed class DeviceSigningKey : IDisposable
{
    private readonly ECDsa _key;

    public DeviceSigningKey(string keyId, ECDsa key, bool canSign)
    {
        KeyId = keyId;
        _key = key;
        CanSign = canSign;
        SecurityKey = new ECDsaSecurityKey(key) { KeyId = keyId };
    }

    public string KeyId { get; }

    public bool CanSign { get; }

    public ECDsaSecurityKey SecurityKey { get; }

    public void Dispose() => _key.Dispose();
}

/// <summary>
/// The JSON document that holds the signing keys of the device token issuer: the current key with
/// its private part (PKCS#8, base64url) and the public parts (SubjectPublicKeyInfo, base64url) of
/// the keys that issued tokens before the last rotation. Tokens live fifteen minutes, so a
/// previous key is needed for that long after a rotation.
/// </summary>
public sealed class DeviceTokenKeySet
{
    [JsonPropertyName("current")]
    public CurrentKey? Current { get; set; }

    [JsonPropertyName("previous")]
    public List<PreviousKey>? Previous { get; set; }

    public sealed class CurrentKey
    {
        [JsonPropertyName("kid")]
        public string? Kid { get; set; }

        [JsonPropertyName("privateKey")]
        public string? PrivateKey { get; set; }
    }

    public sealed class PreviousKey
    {
        [JsonPropertyName("kid")]
        public string? Kid { get; set; }

        [JsonPropertyName("publicKey")]
        public string? PublicKey { get; set; }
    }

    /// <summary>
    /// A key set with one newly generated current key. The key id derives from the public key.
    /// </summary>
    public static DeviceTokenKeySet Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        return new DeviceTokenKeySet
        {
            Current = new CurrentKey
            {
                Kid = WebEncoders.Base64UrlEncode(SHA256.HashData(spki)[..8]),
                PrivateKey = WebEncoders.Base64UrlEncode(key.ExportPkcs8PrivateKey()),
            },
            Previous = [],
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// The validated configuration of the device token issuer from the section "DeviceTokens". The
/// signing keys come from a JSON key set (see DeviceTokenKeySet) in DeviceTokens:KeySet, or in the
/// file named by DeviceTokens:KeySetFile, and are loaded like the Data Protection key ring: a
/// deployment outside Development refuses to start without them. In Development a missing key
/// set is generated: written to the configured file when one is named, held in memory otherwise.
/// Issuer and audience are the values of DeviceTokens:Issuer and DeviceTokens:Audience; they
/// differ from the identity provider's, so a token of one issuer never validates as the other.
/// </summary>
public sealed class DeviceTokenSettings : IDisposable
{
    public const string DefaultIssuer = "cichlids-device-tokens";
    public const string DefaultAudience = "cichlids-api-device";

    /// <summary>How long a device token is valid.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private const string Section = "DeviceTokens";
    private const string Es256Curve = "1.2.840.10045.3.1.7";

    private DeviceTokenSettings(string issuer, string audience, DeviceSigningKey current, IReadOnlyList<DeviceSigningKey> validationKeys)
    {
        Issuer = issuer;
        Audience = audience;
        Current = current;
        ValidationKeys = validationKeys;
    }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>The key that signs new tokens.</summary>
    public DeviceSigningKey Current { get; }

    /// <summary>The current key and the previous keys; the keys that validate tokens.</summary>
    public IReadOnlyList<DeviceSigningKey> ValidationKeys { get; }

    /// <summary>
    /// Reads and validates the configuration for the given host environment name and throws an
    /// exception naming the configuration key when the key set is missing outside Development or
    /// invalid.
    /// </summary>
    public static DeviceTokenSettings Resolve(
        IConfiguration configuration, string? environmentName, string contentRootPath, string? authority = null)
    {
        var section = configuration.GetSection(Section);
        var issuer = string.IsNullOrWhiteSpace(section["Issuer"]) ? DefaultIssuer : section["Issuer"]!.Trim();
        var audience = string.IsNullOrWhiteSpace(section["Audience"]) ? DefaultAudience : section["Audience"]!.Trim();
        if (authority is not null && string.Equals(issuer.TrimEnd('/'), authority.TrimEnd('/'), StringComparison.Ordinal))
        {
            throw Refuse(environmentName, $"{Section}:Issuer must differ from the issuer of the identity provider.");
        }

        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        var keySet = ReadKeySet(section, isDevelopment, environmentName, contentRootPath);
        return new DeviceTokenSettings(issuer, audience, Load(keySet, environmentName, out var all), all);
    }

    public void Dispose()
    {
        foreach (var key in ValidationKeys)
        {
            key.Dispose();
        }
    }

    private static DeviceTokenKeySet ReadKeySet(
        IConfigurationSection section, bool isDevelopment, string? environmentName, string contentRootPath)
    {
        var inline = section["KeySet"];
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return Parse(inline, $"{Section}:KeySet", environmentName);
        }

        var file = section["KeySetFile"];
        if (string.IsNullOrWhiteSpace(file))
        {
            return isDevelopment
                ? DeviceTokenKeySet.Generate()
                : throw Refuse(environmentName, $"{Section}:KeySet or {Section}:KeySetFile is not set. Create a key set with tools/create-device-token-key.cs.");
        }

        var path = Path.GetFullPath(file, contentRootPath);
        if (File.Exists(path))
        {
            return Parse(File.ReadAllText(path), $"{Section}:KeySetFile", environmentName);
        }

        if (!isDevelopment)
        {
            throw Refuse(environmentName, $"{Section}:KeySetFile names '{path}', which does not exist.");
        }

        var generated = DeviceTokenKeySet.Generate();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.Write(generated.ToJson());
            return generated;
        }
        catch (IOException) when (File.Exists(path))
        {
            // A second process of the development environment created the file first.
            return Parse(File.ReadAllText(path), $"{Section}:KeySetFile", environmentName);
        }
    }

    private static DeviceTokenKeySet Parse(string json, string source, string? environmentName)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceTokenKeySet>(json)
                ?? throw Refuse(environmentName, $"{source} is empty.");
        }
        catch (JsonException)
        {
            throw Refuse(environmentName, $"{source} is not a JSON key set with the members current and previous.");
        }
    }

    private static DeviceSigningKey Load(DeviceTokenKeySet keySet, string? environmentName, out IReadOnlyList<DeviceSigningKey> all)
    {
        if (keySet.Current is not { Kid: { Length: > 0 } kid, PrivateKey: { Length: > 0 } privateKey })
        {
            throw Refuse(environmentName, $"{Section} key set needs a current key with the members kid and privateKey.");
        }

        var keys = new List<DeviceSigningKey>();
        try
        {
            var current = ECDsa.Create();
            try
            {
                current.ImportPkcs8PrivateKey(WebEncoders.Base64UrlDecode(privateKey), out _);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                current.Dispose();
                throw Refuse(environmentName, $"{Section} current key is not a PKCS#8 private key in base64url.");
            }

            RequireP256(current, "current", environmentName);
            keys.Add(new DeviceSigningKey(kid, current, canSign: true));

            foreach (var previous in keySet.Previous ?? [])
            {
                if (previous is not { Kid: { Length: > 0 } previousKid, PublicKey: { Length: > 0 } publicKey })
                {
                    throw Refuse(environmentName, $"{Section} previous keys need the members kid and publicKey.");
                }

                if (keys.Any(k => k.KeyId == previousKid))
                {
                    throw Refuse(environmentName, $"{Section} key id '{previousKid}' is used twice.");
                }

                var key = ECDsa.Create();
                try
                {
                    key.ImportSubjectPublicKeyInfo(WebEncoders.Base64UrlDecode(publicKey), out _);
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException)
                {
                    key.Dispose();
                    throw Refuse(environmentName, $"{Section} previous key '{previousKid}' is not a SubjectPublicKeyInfo public key in base64url.");
                }

                RequireP256(key, previousKid, environmentName);
                keys.Add(new DeviceSigningKey(previousKid, key, canSign: false));
            }
        }
        catch
        {
            foreach (var key in keys)
            {
                key.Dispose();
            }

            throw;
        }

        all = keys;
        return keys[0];
    }

    private static void RequireP256(ECDsa key, string name, string? environmentName)
    {
        if (key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != Es256Curve)
        {
            key.Dispose();
            throw Refuse(environmentName, $"{Section} key '{name}' is not on the curve P-256.");
        }
    }

    private static InvalidOperationException Refuse(string? environmentName, string reason) =>
        new($"Invalid device token configuration for environment '{environmentName}': {reason}");
}
