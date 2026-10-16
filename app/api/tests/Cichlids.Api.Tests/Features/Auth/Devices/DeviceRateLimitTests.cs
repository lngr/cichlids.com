// Story: task-7.1
using System.Net;
using System.Net.Http.Json;
using Cichlids.Api.Features.Auth.Devices;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// The per-address limits of key registrations and of challenge and token requests.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-7.1")]
public sealed class DeviceRateLimitTests(ApiFixture fixture)
{
    [Fact]
    public async Task Registrations_beyond_the_hourly_limit_of_one_address_are_refused()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DeviceAuth:MaxRegistrationsPerAddressPerHour", "2"));
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            using var device = new TestDevice(client);
            last = await client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey });
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
        using var body = System.Text.Json.JsonDocument.Parse(await last!.Content.ReadAsStringAsync());
        Assert.Equal(DeviceAuthEndpoints.RateLimitedCode, body.RootElement.GetProperty("code").GetString());
        Assert.True(last.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Challenge_requests_beyond_the_limit_of_one_address_are_refused()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DeviceAuth:MaxTokenRequestsPerAddressPerMinute", "2"));
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsync("/api/auth/device/challenges", null)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Token_requests_count_against_the_same_limit_as_challenge_requests()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DeviceAuth:MaxTokenRequestsPerAddressPerMinute", "2"));
        using var client = factory.CreateClient();
        await client.PostAsync("/api/auth/device/challenges", null);
        await client.PostAsync("/api/auth/device/challenges", null);

        var response = await client.PostAsJsonAsync("/api/auth/device/tokens", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "20010DB800010002")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "20010DB800010002")]
    public void Addresses_are_counted_by_their_address_or_their_ipv6_network(string address, string expected)
    {
        Assert.Equal(expected, DeviceAuthEndpoints.AddressKey(System.Net.IPAddress.Parse(address)));
    }

    [Fact]
    public void A_missing_address_is_counted_under_one_key()
    {
        Assert.Equal("unknown", DeviceAuthEndpoints.AddressKey(null));
    }
}
