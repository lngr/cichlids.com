#:sdk Microsoft.NET.Sdk.Web
// Adds a new key to a Data Protection key ring directory for the API, which reads its key ring
// from a secret and never writes to it outside Development. The new key becomes the key that
// encrypts from now on; the keys already in the directory keep decrypting what they encrypted.
// The directory's files form the content of the key ring secret.
//
// Usage: dotnet run tools/create-data-protection-key.cs -- <directory> [<years valid>]
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

if (args.Length is < 1 or > 2 || (args.Length == 2 && (!int.TryParse(args[1], out var parsed) || parsed < 1)))
{
    Console.Error.WriteLine("Usage: dotnet run tools/create-data-protection-key.cs -- <directory> [<years valid, default 5>]");
    return 2;
}

var directory = Directory.CreateDirectory(args[0]);
var years = args.Length == 2 ? int.Parse(args[1]) : 5;

var services = new ServiceCollection();
services.AddDataProtection()
    .SetApplicationName("cichlids-api")
    .PersistKeysToFileSystem(directory)
    .DisableAutomaticKeyGeneration();
// The tool is no web host; its own service provider is the only one.
#pragma warning disable ASP0000
using var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

var now = DateTimeOffset.UtcNow;
var key = provider.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddYears(years));
Console.WriteLine($"Created key {key.KeyId} in {directory.FullName}, valid until {key.ExpirationDate:yyyy-MM-dd}.");
return 0;
