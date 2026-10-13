using Cichlids.Api.Features.Auth.WebSessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The key ring of ASP.NET Core Data Protection that encrypts the stored web session tokens:
/// outside Development the API needs an existing key ring directory and only reads it, so a key
/// ring mounted read-only from a secret works; Development may use a local directory relative to
/// the content root and creates its keys there.
/// </summary>
[Trait("Story", "task-3.41")]
public sealed class KeyRingSettingsTests : IDisposable
{
    private const string ContentRoot = "/srv/cichlids-api";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cichlids-key-ring-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Staging_refuses_to_start_without_a_key_ring_directory(string? path)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            KeyRingSettings.Resolve(Configuration(("DataProtection:KeyRingPath", path)), "Staging", ContentRoot));

        Assert.Contains("DataProtection:KeyRingPath", exception.Message);
    }

    [Fact]
    public void Staging_refuses_a_key_ring_directory_that_does_not_exist()
    {
        var missing = Path.Combine(_directory.FullName, "missing");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            KeyRingSettings.Resolve(Configuration(("DataProtection:KeyRingPath", missing)), "Production", ContentRoot));

        Assert.Contains("DataProtection:KeyRingPath", exception.Message);
    }

    [Fact]
    public void Staging_reads_an_existing_key_ring_without_creating_keys()
    {
        var settings = KeyRingSettings.Resolve(
            Configuration(("DataProtection:KeyRingPath", _directory.FullName)), "Staging", ContentRoot);

        Assert.Equal(_directory.FullName, settings.Path);
        Assert.False(settings.CreatesKeys);
    }

    [Fact]
    public void Development_resolves_a_relative_directory_against_the_content_root_and_creates_keys()
    {
        var settings = KeyRingSettings.Resolve(
            Configuration(("DataProtection:KeyRingPath", "../keys")), "Development", ContentRoot);

        Assert.Equal(Path.GetFullPath("/srv/keys"), settings.Path);
        Assert.True(settings.CreatesKeys);
    }

    [Fact]
    public void Development_without_a_directory_uses_the_default_key_location()
    {
        var settings = KeyRingSettings.Resolve(Configuration(), "Development", ContentRoot);

        Assert.Null(settings.Path);
        Assert.True(settings.CreatesKeys);
    }

    [Fact]
    public void A_read_only_key_ring_encrypts_with_the_key_of_the_operator_and_writes_no_key()
    {
        using (var writer = Provider(services => services.AddDataProtection().PersistKeysToFileSystem(_directory)))
        {
            // A key close to its expiry would make an API that creates keys add a new one.
            var now = DateTimeOffset.UtcNow;
            writer.GetRequiredService<IKeyManager>().CreateNewKey(now.AddDays(-200), now.AddDays(1));
        }

        var keyFiles = _directory.GetFiles().Select(file => file.Name).ToList();
        var settings = KeyRingSettings.Resolve(
            Configuration(("DataProtection:KeyRingPath", _directory.FullName)), "Production", ContentRoot);

        using var reader = Provider(services => services.AddKeyRing(settings));
        var protector = reader.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");

        Assert.Equal("token", protector.Unprotect(protector.Protect("token")));
        Assert.Equal(keyFiles, _directory.GetFiles().Select(file => file.Name).ToList());
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }
}
