using Cichlids.Api.Features.WebPush;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// The Web Push configuration: the VAPID key pair comes from configuration or from a key file, a
/// deployment outside Development that enables Web Push without keys refuses to start, and the
/// push service allowlist holds bare host names.
/// </summary>
[Trait("Story", "task-5.12")]
public sealed class WebPushSettingsTests : IDisposable
{
    private const string ContentRoot = "/srv/cichlids-api";
    private const string Subject = "mailto:operator@example.test";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cichlids-web-push-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void Web_push_is_disabled_unless_configured()
    {
        var settings = WebPushSettings.Resolve(Configuration(), "Production", ContentRoot);

        Assert.False(settings.Enabled);
        Assert.False(settings.Available);
        Assert.Null(settings.PublicKey);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void A_deployment_outside_development_refuses_to_start_when_enabled_without_keys(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WebPushSettings.Resolve(Configuration(("WebPush:Enabled", "true"), ("WebPush:Subject", Subject)), environment, ContentRoot));

        Assert.Contains("WebPush:VapidPublicKey", exception.Message);
        Assert.Contains("WebPush:VapidPrivateKey", exception.Message);
    }

    [Fact]
    public void Development_without_keys_leaves_web_push_unavailable()
    {
        var settings = WebPushSettings.Resolve(Configuration(("WebPush:Enabled", "true")), "Development", ContentRoot);

        Assert.True(settings.Enabled);
        Assert.False(settings.Available);
        Assert.Null(settings.PublicKey);
    }

    [Fact]
    public void Keys_from_configuration_make_web_push_available()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();

        var settings = WebPushSettings.Resolve(Configuration(
            ("WebPush:Enabled", "true"), ("WebPush:VapidPublicKey", publicKey), ("WebPush:VapidPrivateKey", privateKey), ("WebPush:Subject", Subject)),
            "Production", ContentRoot);

        Assert.True(settings.Available);
        Assert.Equal(publicKey, settings.PublicKey);
        Assert.Equal(privateKey, settings.PrivateKey);
        Assert.Equal(Subject, settings.Subject);
    }

    [Fact]
    public void A_key_file_relative_to_the_content_root_supplies_the_keys()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        File.WriteAllText(Path.Combine(_directory.FullName, "keys.json"), $$"""{ "publicKey": "{{publicKey}}", "privateKey": "{{privateKey}}" }""");

        var settings = WebPushSettings.Resolve(Configuration(
            ("WebPush:Enabled", "true"), ("WebPush:VapidKeyFile", "keys.json"), ("WebPush:Subject", Subject)),
            "Development", _directory.FullName);

        Assert.True(settings.Available);
        Assert.Equal(publicKey, settings.PublicKey);
    }

    [Fact]
    public void A_private_key_that_does_not_belong_to_the_public_key_is_refused()
    {
        var (publicKey, _) = VapidKeys.Generate();
        var (_, otherPrivateKey) = VapidKeys.Generate();

        var exception = Assert.Throws<InvalidOperationException>(() => WebPushSettings.Resolve(Configuration(
            ("WebPush:Enabled", "true"), ("WebPush:VapidPublicKey", publicKey), ("WebPush:VapidPrivateKey", otherPrivateKey), ("WebPush:Subject", Subject)),
            "Development", ContentRoot));

        Assert.Contains("WebPush:VapidPrivateKey", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("operator@example.test")]
    [InlineData("http://example.test")]
    [InlineData("mailto:")]
    public void Keys_need_a_contact_subject_as_a_mailto_or_https_address(string? subject)
    {
        var (publicKey, privateKey) = VapidKeys.Generate();

        var exception = Assert.Throws<InvalidOperationException>(() => WebPushSettings.Resolve(Configuration(
            ("WebPush:Enabled", "true"), ("WebPush:VapidPublicKey", publicKey), ("WebPush:VapidPrivateKey", privateKey), ("WebPush:Subject", subject)),
            "Production", ContentRoot));

        Assert.Contains("WebPush:Subject", exception.Message);
    }

    [Theory]
    [InlineData("https://push.example.test")]
    [InlineData("*.example.test")]
    [InlineData("push.example.test:443")]
    [InlineData("")]
    public void An_allowlist_entry_that_is_not_a_bare_host_is_refused(string entry)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => WebPushSettings.Resolve(
            Configuration(("WebPush:AllowedPushHosts:0", entry)), "Development", ContentRoot));

        Assert.Contains("WebPush:AllowedPushHosts", exception.Message);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://web.push.apple.com/abc", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://evil.example.test/fcm.googleapis.com", false)]
    [InlineData("https://notfcm.googleapis.com.evil.test/abc", false)]
    [InlineData("https://xfcm.googleapis.com/abc", false)]
    [InlineData("https://user@fcm.googleapis.com/abc", false)]
    [InlineData("https://fcm.googleapis.com:8443/abc", false)]
    [InlineData("https://127.0.0.1/abc", false)]
    [InlineData("not a url", false)]
    public void The_default_allowlist_admits_the_push_services_of_the_major_browsers_only(string endpoint, bool allowed)
    {
        var settings = WebPushSettings.Resolve(Configuration(), "Production", ContentRoot);

        Assert.Equal(allowed, settings.IsAllowedEndpoint(endpoint));
    }

    [Fact]
    public void A_configured_allowlist_replaces_the_default()
    {
        var settings = WebPushSettings.Resolve(Configuration(("WebPush:AllowedPushHosts:0", "push.example.test")), "Development", ContentRoot);

        Assert.True(settings.IsAllowedEndpoint("https://push.example.test/a"));
        Assert.True(settings.IsAllowedEndpoint("https://eu.push.example.test/a"));
        Assert.False(settings.IsAllowedEndpoint("https://fcm.googleapis.com/fcm/send/abc"));
    }
}
