using Cichlids.Api.Features.Auth;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests;

/// <summary>
/// Outside the Development environment the API validates tokens only against an HTTPS authority
/// and a configured audience, so a deployment with a missing or insecure value must be rejected
/// when the settings are resolved, naming the offending configuration key.
/// </summary>
[Trait("Story", "task-2.27")]
public sealed class AuthenticationSettingsTests
{
    private const string StagingAuthority = "https://auth.staging.cichlids.com/realms/cichlids";

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Staging_rejects_a_missing_or_blank_authority(string? authority)
    {
        var configuration = Configuration(
            ("Authentication:Authority", authority),
            ("Authentication:Audience", "cichlids-api"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationSettings.Resolve(configuration, "Staging"));

        Assert.Contains("Authentication:Authority", exception.Message);
    }

    [Theory]
    [InlineData("http://auth.staging.cichlids.com/realms/cichlids")]
    [InlineData("/realms/cichlids")]
    public void Staging_rejects_an_authority_that_is_not_an_absolute_https_url(string authority)
    {
        var configuration = Configuration(
            ("Authentication:Authority", authority),
            ("Authentication:Audience", "cichlids-api"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationSettings.Resolve(configuration, "Staging"));

        Assert.Contains("Authentication:Authority", exception.Message);
        Assert.Contains("HTTPS", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Staging_rejects_a_blank_audience(string? audience)
    {
        var configuration = Configuration(
            ("Authentication:Authority", StagingAuthority),
            ("Authentication:Audience", audience));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationSettings.Resolve(configuration, "Staging"));

        Assert.Contains("Authentication:Audience", exception.Message);
    }

    [Fact]
    public void Staging_rejects_disabled_https_metadata()
    {
        var configuration = Configuration(
            ("Authentication:Authority", StagingAuthority),
            ("Authentication:Audience", "cichlids-api"),
            ("Authentication:RequireHttpsMetadata", "false"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationSettings.Resolve(configuration, "Staging"));

        Assert.Contains("Authentication:RequireHttpsMetadata", exception.Message);
    }

    [Fact]
    public void Staging_accepts_an_https_authority_and_an_audience()
    {
        var configuration = Configuration(
            ("Authentication:Authority", StagingAuthority),
            ("Authentication:Audience", "cichlids-api"));

        var settings = AuthenticationSettings.Resolve(configuration, "Staging");

        Assert.Equal(StagingAuthority, settings.Authority);
        Assert.Equal("cichlids-api", settings.Audience);
        Assert.True(settings.RequireHttpsMetadata);
    }

    [Fact]
    public void Development_accepts_a_local_http_authority_without_https_metadata()
    {
        var configuration = Configuration(
            ("Authentication:Authority", "http://localhost:8180/realms/cichlids"),
            ("Authentication:Audience", "cichlids-api"),
            ("Authentication:RequireHttpsMetadata", "false"));

        var settings = AuthenticationSettings.Resolve(configuration, "Development");

        Assert.Equal("http://localhost:8180/realms/cichlids", settings.Authority);
        Assert.False(settings.RequireHttpsMetadata);
    }
}
