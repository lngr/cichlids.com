using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests;

/// <summary>
/// The webhook target allowlist accepts only bare host names or IP addresses. A full URL or a
/// wildcard is a configuration mistake that would silently allow nothing, so the host is expected
/// to fail during startup.
/// </summary>
public sealed class WebhookTargetAllowlistStartupTests
{
    [Theory]
    [InlineData("https://x")]
    [InlineData("*.example.test")]
    [InlineData("example.test:8080")]
    public void Host_fails_to_start_with_an_allowlist_entry_that_is_not_a_bare_host(string entry)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The allowlist is read while the application is being built, before the configuration
            // added through ConfigureAppConfiguration is available, so it is set as a host setting.
            builder.UseSetting("Webhooks:AllowedTargetHosts:0", "example.test");
            builder.UseSetting("Webhooks:AllowedTargetHosts:1", entry);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Cichlids"] = "Host=127.0.0.1;Port=1;Database=cichlids;Username=cichlids;Password=cichlids",
                    ["ObjectStorage:ServiceUrl"] = "http://objects.test",
                    ["ObjectStorage:PublicBaseUrl"] = "http://objects.test/cichlids-media",
                    ["OutboxDispatcher:Enabled"] = "false",
                }));
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.Server);

        Assert.Contains("AllowedTargetHosts", exception.ToString());
        Assert.Contains(entry, exception.ToString());
    }
}
