// Creates the VAPID key pair (RFC 8292) with which the API identifies itself to the push services
// of the browsers. The public key goes to the web app, the private key stays on the server.
// The private key is written to a new file readable by its owner only; an existing file is never
// overwritten.
//
// Usage:
//   dotnet run tools/create-vapid-keys.cs -- json <file>
//     Writes {"publicKey": ..., "privateKey": ...}, the format of WebPush:VapidKeyFile.
//   dotnet run tools/create-vapid-keys.cs -- files <directory>
//     Writes the files public-key and private-key, the content of the Kubernetes Secret api-web-push.
using System.Security.Cryptography;

if (args.Length != 2 || args[0] is not ("json" or "files"))
{
    Console.Error.WriteLine("Usage: dotnet run tools/create-vapid-keys.cs -- json <file> | files <directory>");
    return 2;
}

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var parameters = key.ExportParameters(includePrivateParameters: true);
var publicBytes = new byte[65];
publicBytes[0] = 0x04;
parameters.Q.X!.CopyTo(publicBytes, 1);
parameters.Q.Y!.CopyTo(publicBytes, 33);
var publicKey = Base64Url(publicBytes);
var privateKey = Base64Url(parameters.D!);

try
{
    if (args[0] == "json")
    {
        Write(args[1], $"{{\n  \"publicKey\": \"{publicKey}\",\n  \"privateKey\": \"{privateKey}\"\n}}\n");
    }
    else
    {
        Directory.CreateDirectory(args[1]);
        Write(Path.Combine(args[1], "public-key"), publicKey);
        Write(Path.Combine(args[1], "private-key"), privateKey);
    }
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

Console.WriteLine($"Created the VAPID key pair in {Path.GetFullPath(args[1])}.");
Console.WriteLine($"Public key: {publicKey}");
return 0;

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static void Write(string path, string content)
{
    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows())
    {
        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    using var stream = new FileStream(path, options);
    using var writer = new StreamWriter(stream);
    writer.Write(content);
}
