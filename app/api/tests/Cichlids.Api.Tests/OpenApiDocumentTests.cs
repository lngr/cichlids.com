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
    [InlineData("/api/media/photos/{token}")]
    [InlineData("/api/media/videos/{token}/{path}")]
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

    [Theory]
    [InlineData("post", "/api/admin/webhooks")]
    [InlineData("get", "/api/admin/outbox/events")]
    [InlineData("post", "/api/admin/outbox/events/{id}/redeliver")]
    [InlineData("post", "/api/admin/outbox/given-up/redeliver")]
    public async Task Document_ListsEveryAdminEndpoint(string method, string path)
    {
        var response = await fixture.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out var item) && item.TryGetProperty(method, out _),
            $"OpenAPI document is missing {method} {path}.");
    }

    [Theory]
    [InlineData("get", "/api/me")]
    [InlineData("get", "/api/me/drafts")]
    [InlineData("post", "/api/uploads")]
    [InlineData("post", "/api/posts/{id}/publish")]
    [InlineData("delete", "/api/posts/{id}")]
    [InlineData("post", "/api/pictures/{slug}/comments")]
    [InlineData("post", "/api/tanks/{id}/comments")]
    [InlineData("post", "/api/tanks")]
    [InlineData("put", "/api/tanks/{id}/visibility")]
    [InlineData("delete", "/api/comments/{id}")]
    public async Task Document_ListsForbiddenResponseForEveryEndpointThatResolvesTheProfile(string method, string path)
    {
        var response = await fixture.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responses = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        Assert.True(responses.TryGetProperty("403", out _), $"{method} {path} lists no 403 response.");
    }
}
