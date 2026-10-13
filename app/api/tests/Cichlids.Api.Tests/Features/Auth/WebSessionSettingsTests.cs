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
                ("WebSession:SecureCookies", "false"),
                ("WebSession:ClientSecret", "local-secret"),
                ("WebSession:PostLogoutRedirectUrl", "http://localhost:8081/me")),
            "Development");

        Assert.Equal(new Uri("http://localhost:5045/api/auth/callback"), settings.CallbackUrl);
        Assert.Equal(["http://localhost:8081"], settings.AllowedOrigins);
        Assert.False(settings.SecureCookies);
        Assert.Equal("cichlids-session", settings.SessionCookieName);
        Assert.Equal("cichlids-web-bff", settings.ClientId);
        Assert.Equal("local-secret", settings.ClientSecret);
        Assert.Equal(new Uri("http://localhost:8081/me"), settings.PostLogoutRedirectUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://staging.cichlids.com/me")]
    [InlineData("/me")]
    [InlineData("https://elsewhere.example/me")]
    public void Staging_web_sign_in_needs_an_HTTPS_return_address_after_logout_on_an_allowed_origin(string? url)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebSessionSettings.Resolve(
                Configuration(
                    ("WebSession:CallbackUrl", "https://staging.cichlids.com/api/auth/callback"),
                    ("WebSession:AllowedOrigins:0", "https://staging.cichlids.com"),
                    ("WebSession:ClientSecret", "staging-secret"),
                    ("WebSession:PostLogoutRedirectUrl", url)),
                "Staging"));

        Assert.Contains("WebSession:PostLogoutRedirectUrl", exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Web_sign_in_refuses_to_start_without_the_client_secret(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebSessionSettings.Resolve(
                Configuration(
                    ("WebSession:CallbackUrl", "https://staging.cichlids.com/api/auth/callback"),
                    ("WebSession:AllowedOrigins:0", "https://staging.cichlids.com")),
                environment));

        Assert.Contains("WebSession:ClientSecret", exception.Message);
    }

    [Fact]
    public void The_text_of_the_settings_leaves_out_the_client_secret()
    {
        var settings = WebSessionSettings.Resolve(
            Configuration(
                ("WebSession:CallbackUrl", "https://staging.cichlids.com/api/auth/callback"),
                ("WebSession:AllowedOrigins:0", "https://staging.cichlids.com"),
                ("WebSession:ClientId", "custom-bff"),
                ("WebSession:ClientSecret", "very-secret-value"),
                ("WebSession:PostLogoutRedirectUrl", "https://staging.cichlids.com/me")),
            "Staging");

        Assert.Equal("custom-bff", settings.ClientId);
        Assert.DoesNotContain("very-secret-value", settings.ToString());
        Assert.Contains("custom-bff", settings.ToString());
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
