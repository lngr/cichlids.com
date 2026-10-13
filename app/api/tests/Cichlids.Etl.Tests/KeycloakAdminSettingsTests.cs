using Cichlids.Etl.Identity;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Checks which Keycloak admin credentials the settings accept per environment. Each test starts
/// without the CICHLIDS_KEYCLOAK_* variables and restores them afterwards.
/// </summary>
public sealed class KeycloakAdminSettingsTests : IDisposable
{
    private static readonly string[] Variables =
    [
        "CICHLIDS_KEYCLOAK_URL",
        "CICHLIDS_KEYCLOAK_REALM",
        "CICHLIDS_KEYCLOAK_ADMIN_USER",
        "CICHLIDS_KEYCLOAK_ADMIN_PASSWORD",
    ];

    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public KeycloakAdminSettingsTests()
    {
        foreach (var name in Variables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact]
    public void FallsBackToTheLocalAdminInDevelopment()
    {
        var settings = KeycloakAdminSettings.Resolve(Configuration(), "Development");

        Assert.Equal("admin", settings.AdminUser);
        Assert.Equal("admin", settings.AdminPassword);
    }

    [Fact]
    public void AcceptsTheLocalAdminPairInDevelopment()
    {
        var settings = KeycloakAdminSettings.Resolve(Configuration(("Keycloak:AdminUser", "admin"), ("Keycloak:AdminPassword", "admin")), "Development");

        Assert.Equal("admin", settings.AdminPassword);
    }

    [Fact]
    public void RejectsMissingCredentialsOutsideDevelopmentAndNamesEverySetting()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => KeycloakAdminSettings.Resolve(Configuration(), "Staging"));

        Assert.Contains("CICHLIDS_KEYCLOAK_ADMIN_USER", exception.Message);
        Assert.Contains("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD", exception.Message);
        Assert.Contains("Keycloak:AdminUser", exception.Message);
        Assert.Contains("Keycloak:AdminPassword", exception.Message);
    }

    [Fact]
    public void RejectsTheLocalAdminPairOutsideDevelopment()
    {
        var configuration = Configuration(("Keycloak:AdminUser", "admin"), ("Keycloak:AdminPassword", "admin"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => KeycloakAdminSettings.Resolve(configuration, "Staging"));

        Assert.Contains("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD", exception.Message);
    }

    [Fact]
    public void RejectsAPasswordWithoutAUserOutsideDevelopment()
    {
        var configuration = Configuration(("Keycloak:AdminPassword", "a-real-password"));

        Assert.Throws<InvalidOperationException>(() => KeycloakAdminSettings.Resolve(configuration, "Production"));
    }

    [Fact]
    public void AcceptsConfiguredCredentialsOutsideDevelopment()
    {
        var configuration = Configuration(("Keycloak:AdminUser", "ops-admin"), ("Keycloak:AdminPassword", "a-real-password"));

        var settings = KeycloakAdminSettings.Resolve(configuration, "Staging");

        Assert.Equal("ops-admin", settings.AdminUser);
        Assert.Equal("a-real-password", settings.AdminPassword);
    }

    [Fact]
    public void PrefersTheEnvironmentVariablesOverTheConfiguration()
    {
        Environment.SetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_USER", "env-admin");
        Environment.SetEnvironmentVariable("CICHLIDS_KEYCLOAK_ADMIN_PASSWORD", "env-password");
        var configuration = Configuration(("Keycloak:AdminUser", "admin"), ("Keycloak:AdminPassword", "admin"));

        var settings = KeycloakAdminSettings.Resolve(configuration, "Production");

        Assert.Equal("env-admin", settings.AdminUser);
        Assert.Equal("env-password", settings.AdminPassword);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
