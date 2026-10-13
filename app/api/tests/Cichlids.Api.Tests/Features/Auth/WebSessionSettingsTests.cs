using Cichlids.Api.Features.Auth.WebSessions;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The web session settings: Development may run the web session over plain HTTP, every other
/// environment refuses cookies without the Secure flag and a callback that is not HTTPS, and
/// allowed origins must be plain origins.
/// </summary>
[Trait("Story", "task-3.41")]
public sealed class WebSessionSettingsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public void Development_accepts_plain_HTTP_and_cookies_without_the_Secure_flag()
    {
        var settings = WebSessionSettings.Resolve(
            Configuration(
                ("WebSession:CallbackUrl", "http://localhost:5045/api/auth/callback"),
                ("WebSession:AllowedOrigins:0", "http://localhost:8081"),
                ("WebSession:SecureCookies", "false")),
            "Development");

        Assert.Equal(new Uri("http://localhost:5045/api/auth/callback"), settings.CallbackUrl);
        Assert.Equal(["http://localhost:8081"], settings.AllowedOrigins);
        Assert.False(settings.SecureCookies);
        Assert.Equal("cichlids-session", settings.SessionCookieName);
        Assert.Equal("cichlids-app", settings.ClientId);
    }

    [Fact]
    public void Secure_cookies_are_the_default_and_carry_the_host_prefix()
    {
        var settings = WebSessionSettings.Resolve(Configuration(), "Staging");

        Assert.True(settings.SecureCookies);
        Assert.Equal("__Host-cichlids-session", settings.SessionCookieName);
        Assert.Equal("__Host-cichlids-login", settings.LoginCookieName);
        Assert.Null(settings.CallbackUrl);
    }

    [Fact]
    public void Staging_refuses_cookies_without_the_Secure_flag()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebSessionSettings.Resolve(Configuration(("WebSession:SecureCookies", "false")), "Staging"));

        Assert.Contains("WebSession:SecureCookies", exception.Message);
    }

    [Theory]
    [InlineData("http://staging.cichlids.com/api/auth/callback")]
    [InlineData("/api/auth/callback")]
    public void Staging_refuses_a_callback_that_is_not_an_absolute_HTTPS_URL(string callbackUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebSessionSettings.Resolve(Configuration(("WebSession:CallbackUrl", callbackUrl)), "Staging"));

        Assert.Contains("WebSession:CallbackUrl", exception.Message);
    }

    [Theory]
    [InlineData("https://cichlids.com/app")]
    [InlineData("cichlids.com")]
    [InlineData("*")]
    public void Allowed_origins_must_be_plain_origins(string origin)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebSessionSettings.Resolve(Configuration(("WebSession:AllowedOrigins:0", origin)), "Development"));

        Assert.Contains("WebSession:AllowedOrigins", exception.Message);
    }
}
