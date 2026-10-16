// Creates and rotates the signing keys with which the API issues device tokens (ES256).
// The file is a JSON key set with the current key (PKCS#8 private key, base64url) and the public
// keys of the previous ones (SubjectPublicKeyInfo, base64url), the format of DeviceTokens:KeySet
// and DeviceTokens:KeySetFile. The file is written readable by its owner only.
//
// Usage:
//   dotnet run tools/create-device-token-key.cs -- new <file>
//     Writes a key set with one new current key; an existing file is never overwritten.
//   dotnet run tools/create-device-token-key.cs -- rotate <file>
//     Makes a new key the current one and keeps the public part of the former current key as a
//     previous key, so tokens signed before the rotation stay valid until they expire (15 minutes).
//     Previous keys that are older than the one just retired are dropped.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2 || args[0] is not ("new" or "rotate"))
{
    Console.Error.WriteLine("Usage: dotnet run tools/create-device-token-key.cs -- new <file> | rotate <file>");
    return 2;
}

var path = args[1];
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var kid = Base64Url(SHA256.HashData(key.ExportSubjectPublicKeyInfo())[..8]);
var current = new JsonObject { ["kid"] = kid, ["privateKey"] = Base64Url(key.ExportPkcs8PrivateKey()) };
var previous = new JsonArray();

try
{
    if (args[0] == "rotate")
    {
        var existing = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("The file holds no key set.");
        var former = existing["current"]?.AsObject() ?? throw new InvalidOperationException("The key set has no current key.");
        using var formerKey = ECDsa.Create();
        formerKey.ImportPkcs8PrivateKey(Convert.FromBase64String(Pad(former["privateKey"]!.GetValue<string>())), out _);
        JsonNode formerPublic = new JsonObject
        {
            ["kid"] = former["kid"]!.GetValue<string>(),
            ["publicKey"] = Base64Url(formerKey.ExportSubjectPublicKeyInfo()),
        };
        previous.Add(formerPublic);
    }

    var content = new JsonObject { ["current"] = current, ["previous"] = previous }
        .ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    if (args[0] == "new")
    {
        Write(path, content, FileMode.CreateNew);
    }
    else
    {
        var temp = path + ".new";
        Write(temp, content, FileMode.Create);
        File.Move(temp, path, overwrite: true);
    }
}
catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or CryptographicException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

Console.WriteLine($"Wrote the device token key set to {Path.GetFullPath(path)}; the current key id is {kid}.");
return 0;

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static string Pad(string base64Url)
{
    var text = base64Url.Replace('-', '+').Replace('_', '/');
    return text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
}

static void Write(string path, string content, FileMode mode)
{
    var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows())
    {
        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    using var stream = new FileStream(path, options);
    using var writer = new StreamWriter(stream);
    writer.Write(content);
}
