using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cichlids.Api.Tests;

public class HealthzTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_ReturnsOkStatus()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"status\":\"ok\"}", body);
    }
}
