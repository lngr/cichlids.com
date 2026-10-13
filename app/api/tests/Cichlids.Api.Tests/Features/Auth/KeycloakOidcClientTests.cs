using System.Net;
using System.Text;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.WebSessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The classification of Keycloak's answers for web sessions: HTTP 400 and 401 refuse, every
/// other failure leaves Keycloak unavailable, and a discovery document that could not be loaded is
/// loaded again on the next call.
/// </summary>
[Trait("Story", "task-3.41")]
public sealed class KeycloakOidcClientTests
{
    private const string Authority = "https://auth.cichlids.test/realms/cichlids";
    private const string TokenEndpoint = Authority + "/protocol/openid-connect/token";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, TokenRequestOutcome.Refused)]
    [InlineData(HttpStatusCode.Unauthorized, TokenRequestOutcome.Refused)]
    [InlineData(HttpStatusCode.InternalServerError, TokenRequestOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TokenRequestOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TokenRequestOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, TokenRequestOutcome.Unavailable)]
    public async Task Refresh_classifies_the_status_of_the_token_endpoint(HttpStatusCode status, TokenRequestOutcome expected)
    {
        var client = Client(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"x\"}", Encoding.UTF8, "application/json") });

        var result = await client.RefreshAsync("offline", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task Refresh_reports_an_unreachable_token_endpoint_as_unavailable()
    {
        var client = Client(_ => throw new HttpRequestException("Connection refused"));

        Assert.Equal(TokenRequestOutcome.Unavailable, (await client.RefreshAsync("offline", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Refresh_reports_an_unreadable_answer_as_unavailable()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") });

        Assert.Equal(TokenRequestOutcome.Unavailable, (await client.RefreshAsync("offline", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Refresh_returns_the_issued_tokens()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"a\",\"refresh_token\":\"r\",\"id_token\":\"i\",\"expires_in\":300}", Encoding.UTF8, "application/json"),
        });

        var result = await client.RefreshAsync("offline", CancellationToken.None);

        Assert.Equal(TokenRequestOutcome.Issued, result.Outcome);
        Assert.Equal(new IssuedTokens("a", "r", "i", 300), result.Tokens);
    }

    [Fact]
    public async Task A_failed_discovery_is_tried_again_on_the_next_call()
    {
        var discoveryAvailable = false;
        var client = new KeycloakOidcClient(
            new SingleClientFactory(new Handler(request =>
            {
                if (!discoveryAvailable)
                {
                    throw new HttpRequestException("Connection refused");
                }

                return Discovery(request);
            })),
            new AuthenticationSettings(Authority, "cichlids-api", true),
            Settings(),
            NullLogger<KeycloakOidcClient>.Instance);

        await Assert.ThrowsAsync<KeycloakUnavailableException>(() => client.GetEndpointsAsync(CancellationToken.None));
        discoveryAvailable = true;

        var endpoints = await client.GetEndpointsAsync(CancellationToken.None);

        Assert.Equal(new Uri(TokenEndpoint), endpoints.Token);
    }

    private static KeycloakOidcClient Client(Func<HttpRequestMessage, HttpResponseMessage> tokenEndpoint) =>
        new(
            new SingleClientFactory(new Handler(request =>
                request.RequestUri!.AbsoluteUri == TokenEndpoint ? tokenEndpoint(request) : Discovery(request))),
            new AuthenticationSettings(Authority, "cichlids-api", true),
            Settings(),
            NullLogger<KeycloakOidcClient>.Instance);

    private static WebSessionSettings Settings() =>
        new(new Uri("https://cichlids.test/api/auth/callback"), ["https://cichlids.test"], true, "cichlids-app");

    private static HttpResponseMessage Discovery(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $"{{\"authorization_endpoint\":\"{Authority}/protocol/openid-connect/auth\",\"token_endpoint\":\"{TokenEndpoint}\"}}",
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
