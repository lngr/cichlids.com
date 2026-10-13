using System.Net;

namespace Cichlids.Api.Tests;

[Collection(ApiCollection.Name)]
public class DevelopmentCorsTests(ApiFixture fixture)
{
    [Fact]
    public async Task NullOrigin_DoesNotCauseServerErrorOrAllowCors()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/healthz");
        request.Headers.TryAddWithoutValidation("Origin", "null");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        using var response = await fixture.Client.SendAsync(request);

        Assert.False((int)response.StatusCode >= 500);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
