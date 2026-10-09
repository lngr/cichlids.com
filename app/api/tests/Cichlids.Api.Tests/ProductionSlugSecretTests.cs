using Cichlids.Infrastructure.Slugs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cichlids.Api.Tests;

/// <summary>
/// A deployment outside Development must never mint slugs, generated handles, login usernames or
/// guest names under no secret or under the well-known local development one, since either would
/// let a later deployment mint different values for the same legacy row. The host is expected to
/// fail during startup rather than on the first request that needs a generated value.
/// </summary>
public sealed class ProductionSlugSecretTests
{
    [Fact]
    public void Host_fails_to_start_in_production_without_a_slug_secret()
    {
        var originalSecret = Environment.GetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable);
        Environment.SetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable, null);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
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
                        ["Authentication:Authority"] = "https://issuer.test",
                        ["Authentication:Audience"] = "cichlids-api",
                        ["Authentication:RequireHttpsMetadata"] = "false",
                        ["OutboxDispatcher:Enabled"] = "false",
                    }));
            });

            var exception = Assert.ThrowsAny<Exception>(() => factory.Server);

            Assert.Contains(SlugGenerator.SecretEnvironmentVariable, exception.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SlugGenerator.SecretEnvironmentVariable, originalSecret);
        }
    }
}
