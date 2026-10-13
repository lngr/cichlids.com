using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests;

/// <summary>
/// A deployment outside Development must not start without an authority to validate tokens
/// against. The host fails during startup and the message names the missing setting.
/// </summary>
[Trait("Story", "task-2.27")]
public sealed class StagingAuthenticationStartupTests
{
    [Fact]
    public void Host_fails_to_start_in_staging_without_an_authority_and_names_the_setting()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            // Host settings reach the configuration before the authentication settings are
            // resolved. The slug secret is set and the authority is left out.
            builder.UseSetting("Slugs:Secret", "staging-startup-test-secret");
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

        Assert.Contains("Authentication:Authority", exception.ToString());
    }
}
