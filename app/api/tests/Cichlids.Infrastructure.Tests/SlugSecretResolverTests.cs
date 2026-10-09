using Cichlids.Infrastructure.Slugs;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Infrastructure.Tests;

public sealed class SlugSecretResolverTests
{
    [Fact]
    public void Resolve_in_development_returns_the_configured_secret()
    {
        var configuration = ConfigurationWithSecret("a-real-secret");

        var secret = SlugSecretResolver.Resolve(configuration, "Development");

        Assert.Equal("a-real-secret", secret);
    }

    [Fact]
    public void Resolve_in_development_falls_back_to_the_development_default_when_no_secret_is_configured()
    {
        var configuration = ConfigurationWithSecret(null);

        var secret = SlugSecretResolver.Resolve(configuration, "Development");

        Assert.Equal(SlugSecretResolver.DevelopmentDefault, secret);
    }

    [Fact]
    public void Resolve_outside_development_throws_when_no_secret_is_configured()
    {
        var configuration = ConfigurationWithSecret(null);

        var exception = Assert.Throws<InvalidOperationException>(() => SlugSecretResolver.Resolve(configuration, "Production"));

        Assert.Contains(SlugGenerator.SecretEnvironmentVariable, exception.Message);
    }

    [Fact]
    public void Resolve_outside_development_throws_when_the_configured_secret_is_the_development_default()
    {
        var configuration = ConfigurationWithSecret(SlugSecretResolver.DevelopmentDefault);

        var exception = Assert.Throws<InvalidOperationException>(() => SlugSecretResolver.Resolve(configuration, "Production"));

        Assert.Contains(SlugGenerator.SecretEnvironmentVariable, exception.Message);
    }

    [Fact]
    public void Resolve_outside_development_returns_a_real_configured_secret()
    {
        var configuration = ConfigurationWithSecret("a-real-secret");

        var secret = SlugSecretResolver.Resolve(configuration, "Production");

        Assert.Equal("a-real-secret", secret);
    }

    [Fact]
    public void Resolve_prefers_the_environment_variable_over_configuration()
    {
        var configuration = ConfigurationWithSecret("from-configuration");
        Environment.SetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable, "from-environment");
        try
        {
            var secret = SlugSecretResolver.Resolve(configuration, "Production");

            Assert.Equal("from-environment", secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable, null);
        }
    }

    private static IConfiguration ConfigurationWithSecret(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SlugGenerator.SecretConfigurationKey] = secret })
            .Build();
}
