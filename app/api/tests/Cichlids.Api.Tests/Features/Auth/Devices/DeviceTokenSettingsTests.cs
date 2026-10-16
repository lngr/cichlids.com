// Story: task-7.1
using System.Security.Cryptography;
using Cichlids.Api.Features.Auth.Devices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// Loading the signing keys of the device tokens: required outside Development, generated in
/// Development, validated, and rotated with a previous key.
/// </summary>
[Trait("Story", "task-7.1")]
public sealed class DeviceTokenSettingsTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cichlids-device-keys-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Outside_development_a_missing_key_set_refuses_to_start_and_names_the_setting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve([], "Production"));

        Assert.Contains("DeviceTokens:KeySet", exception.Message);
    }

    [Fact]
    public void Outside_development_a_missing_key_file_refuses_to_start()
    {
        var path = Path.Combine(_directory.FullName, "missing.json");

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["DeviceTokens:KeySetFile"] = path }, "Staging"));

        Assert.Contains("DeviceTokens:KeySetFile", exception.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Outside_development_a_key_set_in_a_file_is_read()
    {
        var path = Path.Combine(_directory.FullName, "keys.json");
        File.WriteAllText(path, DeviceTokenKeySet.Generate().ToJson());

        using var settings = Resolve(new() { ["DeviceTokens:KeySetFile"] = path }, "Production");

        Assert.True(settings.Current.CanSign);
        Assert.Single(settings.ValidationKeys);
    }

    [Fact]
    public void An_inline_key_set_takes_precedence_over_the_file()
    {
        var path = Path.Combine(_directory.FullName, "keys.json");
        File.WriteAllText(path, DeviceTokenKeySet.Generate().ToJson());

        using var settings = Resolve(new() { ["DeviceTokens:KeySet"] = DeviceTestKeys.KeySetJson, ["DeviceTokens:KeySetFile"] = path }, "Production");

        Assert.Equal(DeviceTestKeys.CurrentKeyId, settings.Current.KeyId);
        Assert.Equal([DeviceTestKeys.CurrentKeyId, DeviceTestKeys.PreviousKeyId], settings.ValidationKeys.Select(k => k.KeyId));
        Assert.False(settings.ValidationKeys[1].CanSign);
    }

    [Fact]
    public void Development_generates_a_key_in_memory_when_none_is_configured()
    {
        using var first = Resolve([], "Development");
        using var second = Resolve([], "Development");

        Assert.True(first.Current.CanSign);
        Assert.NotEqual(first.Current.KeyId, second.Current.KeyId);
    }

    [Fact]
    public void Development_writes_a_missing_key_file_once_and_reads_it_afterwards()
    {
        var path = Path.Combine(_directory.FullName, "nested", "keys.json");

        using var first = Resolve(new() { ["DeviceTokens:KeySetFile"] = path }, "Development");
        using var second = Resolve(new() { ["DeviceTokens:KeySetFile"] = path }, "Development");

        Assert.True(File.Exists(path));
        Assert.Equal(first.Current.KeyId, second.Current.KeyId);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void A_token_of_the_issuer_validates_with_a_rotated_key_set_that_keeps_the_old_key()
    {
        using var oldSettings = Resolve([], "Development");
        var oldToken = new DeviceTokenIssuer(oldSettings).Issue(Guid.NewGuid(), 5, DateTimeOffset.UtcNow);
        var rotated = DeviceTokenKeySet.Generate();
        rotated.Previous!.Add(new DeviceTokenKeySet.PreviousKey
        {
            Kid = oldSettings.Current.KeyId,
            PublicKey = WebEncoders.Base64UrlEncode(oldSettings.Current.SecurityKey.ECDsa.ExportSubjectPublicKeyInfo()),
        });

        using var settings = Resolve(new() { ["DeviceTokens:KeySet"] = rotated.ToJson() }, "Production");

        Assert.NotEqual(oldSettings.Current.KeyId, settings.Current.KeyId);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(oldToken.AccessToken);
        Assert.Contains(settings.ValidationKeys, k => k.KeyId == token.Kid);
    }

    [Fact]
    public void A_key_that_is_not_on_p256_is_refused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var keySet = new DeviceTokenKeySet
        {
            Current = new() { Kid = "k", PrivateKey = WebEncoders.Base64UrlEncode(p384.ExportPkcs8PrivateKey()) },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["DeviceTokens:KeySet"] = keySet.ToJson() }, "Production"));

        Assert.Contains("P-256", exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"current":{"kid":"k"}}""")]
    [InlineData("""{"current":{"kid":"k","privateKey":"AAAA"}}""")]
    public void An_unreadable_key_set_is_refused(string json)
    {
        Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["DeviceTokens:KeySet"] = json }, "Production"));
    }

    [Fact]
    public void A_repeated_key_id_is_refused()
    {
        var keySet = DeviceTokenKeySet.Generate();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        keySet.Previous!.Add(new() { Kid = keySet.Current!.Kid, PublicKey = WebEncoders.Base64UrlEncode(other.ExportSubjectPublicKeyInfo()) });

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["DeviceTokens:KeySet"] = keySet.ToJson() }, "Production"));

        Assert.Contains("used twice", exception.Message);
    }

    [Fact]
    public void An_issuer_equal_to_the_identity_provider_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DeviceTokenSettings.Resolve(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceTokens:Issuer"] = "https://auth.example.test/realms/cichlids" }).Build(),
            "Development", _directory.FullName, authority: "https://auth.example.test/realms/cichlids/"));

        Assert.Contains("DeviceTokens:Issuer", exception.Message);
    }

    [Fact]
    public void The_host_refuses_to_start_in_staging_without_a_key_set()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("Authentication:Authority", "https://auth.example.test/realms/cichlids");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Cichlids"] = "Host=127.0.0.1;Port=5432;Database=cichlids;Username=cichlids;Password=cichlids",
                    ["ObjectStorage:ServiceUrl"] = "http://objects.test",
                    ["ObjectStorage:Region"] = "us-east-1",
                    ["ObjectStorage:Bucket"] = "cichlids-media",
                    ["ObjectStorage:AccessKey"] = "test",
                    ["ObjectStorage:SecretKey"] = "test",
                    ["ObjectStorage:ForcePathStyle"] = "true",
                    ["ObjectStorage:PublicBaseUrl"] = "http://objects.test/cichlids-media",
                    ["OutboxDispatcher:Enabled"] = "false",
                }));
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.Server);

        Assert.Contains("DeviceTokens:KeySet", exception.ToString());
    }

    [Fact]
    public void The_token_lifetime_is_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), DeviceTokenSettings.TokenLifetime);
    }

    private DeviceTokenSettings Resolve(Dictionary<string, string?> values, string environment) =>
        DeviceTokenSettings.Resolve(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), environment, _directory.FullName);
}
