using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Webhooks;

[Collection(ApiCollection.Name)]
public class WebhookAdminEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task PostWebhook_AnonymousReturnsUnauthorized()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/admin/webhooks", new CreateWebhookRequest("https://example.test/hook", "s3cret", ["comment.created"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_WithoutAdminRoleReturnsForbidden()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plain-{Guid.NewGuid():N}"[..24]);

        var response = await SendPostAsync(
            token, new CreateWebhookRequest("https://example.test/hook", "s3cret", ["comment.created"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_ModeratorRoleAloneReturnsForbidden()
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendPostAsync(
            token, new CreateWebhookRequest("https://example.test/hook", "s3cret", ["comment.created"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWebhooks_WithoutAdminRoleReturnsForbidden()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plain-{Guid.NewGuid():N}"[..24]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/webhooks");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_AsAdminCreatesSubscriptionWithoutEchoingTheSecret()
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), $"admin-{Guid.NewGuid():N}"[..24], roles: ["admin"]);

        var response = await SendPostAsync(
            token, new CreateWebhookRequest("https://example.test/hook", "s3cret", ["comment.created", "rating.created"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("s3cret", body);

        var created = JsonSerializer.Deserialize<WebhookDto>(body, TestJson.Options)!;
        Assert.Equal("https://example.test/hook", created.Url);
        Assert.Equal(["comment.created", "rating.created"], created.EventTypes);
        Assert.True(created.Active);

        await using var db = fixture.CreateDbContext();
        var stored = await db.WebhookSubscriptions.SingleAsync(w => w.Id == created.Id);
        Assert.Equal("s3cret", stored.Secret);
    }

    [Theory]
    [InlineData(null, "s3cret", new[] { "comment.created" })]
    [InlineData("not-a-url", "s3cret", new[] { "comment.created" })]
    [InlineData("https://example.test/hook", "", new[] { "comment.created" })]
    [InlineData("https://example.test/hook", "s3cret", new string[0])]
    public async Task PostWebhook_InvalidPayloadReturnsBadRequest(string? url, string secret, string[] eventTypes)
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), $"admin-{Guid.NewGuid():N}"[..24], roles: ["admin"]);

        var response = await SendPostAsync(token, new CreateWebhookRequest(url, secret, eventTypes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWebhooks_AsAdminListsCreatedSubscriptions()
    {
        var adminToken = TestTokens.Create(
            Guid.NewGuid().ToString(), $"admin-{Guid.NewGuid():N}"[..24], roles: ["admin"]);

        var created = await SendPostAsync(
            adminToken, new CreateWebhookRequest("https://example.test/list-hook", "s3cret", ["*"]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdDto = JsonSerializer.Deserialize<WebhookDto>(
            await created.Content.ReadAsStringAsync(), TestJson.Options)!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/webhooks");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = JsonSerializer.Deserialize<List<WebhookDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Contains(list, w => w.Id == createdDto.Id && w.Url == "https://example.test/list-hook");
    }

    private async Task<HttpResponseMessage> SendPostAsync(string token, CreateWebhookRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/webhooks") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }
}
