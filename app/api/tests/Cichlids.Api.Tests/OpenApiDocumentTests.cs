using System.Text.Json;

namespace Cichlids.Api.Tests;

[Collection(ApiCollection.Name)]
public class OpenApiDocumentTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("/api/species")]
    [InlineData("/api/species/{idOrSlug}")]
    [InlineData("/api/pictures")]
    [InlineData("/api/pictures/{slug}")]
    [InlineData("/api/pictures/{slug}/comments")]
    [InlineData("/api/tanks")]
    [InlineData("/api/tanks/{id}")]
    [InlineData("/api/tanks/{id}/comments")]
    [InlineData("/api/profiles/{id}")]
    [InlineData("/api/profiles/{id}/pictures")]
    [InlineData("/api/profiles/{id}/tanks")]
    [InlineData("/api/community/categories")]
    [InlineData("/api/community/threads")]
    [InlineData("/api/community/threads/{id}")]
    public async Task Document_ListsEveryReadEndpointPath(string path)
    {
        var response = await fixture.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI document is missing path '{path}'.");
    }
}
