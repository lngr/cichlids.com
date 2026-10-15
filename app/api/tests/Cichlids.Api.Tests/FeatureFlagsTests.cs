using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cichlids.Api.Tests;

public sealed class FeatureFlagsTests
{
    private static WebApplicationFactory<Program> CreateFactory(string name, string? value) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Cichlids", "Host=127.0.0.1;Port=1;Database=feature_flags_test;Username=test;Password=test");
            builder.UseSetting("DraftCleanup:Enabled", "false");
            builder.UseSetting("WebSessionCleanup:Enabled", "false");
            builder.UseSetting("OutboxDispatcher:Enabled", "false");
            if (value is not null) builder.UseSetting($"FeatureFlags:{name}", value);
        });

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task Anonymous_endpoint_returns_only_known_flags_and_short_public_cache(string? configured, bool expected)
    {
        await using var factory = CreateFactory("discover-activity", configured);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/feature-flags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, bool> { ["discover-activity"] = expected },
            await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>());
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.CacheControl?.MaxAge);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Theory]
    [InlineData("discover-activty", "true")]
    [InlineData("discover-activity", "yes")]
    [InlineData("discover-activity:child", "true")]
    public async Task Invalid_configuration_refuses_startup(string name, string value)
    {
        await using var factory = CreateFactory(name, value);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("FeatureFlags", exception.ToString());
    }
}
