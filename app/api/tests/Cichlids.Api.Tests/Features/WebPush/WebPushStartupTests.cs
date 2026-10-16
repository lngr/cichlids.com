using Microsoft.AspNetCore.Hosting;
using Cichlids.Api.Features.Auth.Devices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// A deployment outside Development that enables Web Push without a VAPID key pair fails during
/// startup, and the message names the missing setting.
/// </summary>
[Trait("Story", "task-5.12")]
public sealed class WebPushStartupTests : IDisposable
{
    private readonly DirectoryInfo _keyRing = Directory.CreateTempSubdirectory("cichlids-web-push-startup-");

    public void Dispose() => _keyRing.Delete(recursive: true);

    [Fact]
    public void Host_fails_to_start_in_staging_with_web_push_enabled_and_no_keys()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("Slugs:Secret", "staging-startup-test-secret");
            builder.UseSetting("Authentication:Authority", "https://auth.example.test/realms/cichlids");
            builder.UseSetting("DataProtection:KeyRingPath", _keyRing.FullName);
            builder.UseSetting("DeviceTokens:KeySet", DeviceTokenKeySet.Generate().ToJson());
            builder.UseSetting("WebPush:Enabled", "true");
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

        Assert.Contains("WebPush:VapidPublicKey", exception.ToString());
    }
}
