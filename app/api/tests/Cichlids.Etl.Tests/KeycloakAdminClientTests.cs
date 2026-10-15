using System.Net;
using System.Text;
using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Checks the error reporting and the retry behavior of the Keycloak admin client against a stub
/// HTTP handler, so no Keycloak server is involved.
/// </summary>
public sealed class KeycloakAdminClientTests
{
    private const string TokenBody = """{"access_token":"token-1","expires_in":300}""";

    [Fact]
    public async Task ReportsOperationAndStatusWithoutTheResponseBody()
    {
        const string body = """{"errorMessage":"User exists with same username: jdoe or email: jdoe@example.test"}""";
        var handler = new StubHandler(request => IsTokenRequest(request) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.Conflict, body));
        using var client = CreateClient(handler, new List<TimeSpan>());

        var exception = await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Contains("409", exception.Message);
        Assert.Contains("list users", exception.Message);
        Assert.DoesNotContain("jdoe", exception.Message);
        Assert.DoesNotContain("@", exception.Message);
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    [Fact]
    public async Task ReportsTheErrorCodeButNotTheErrorDescription()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"Invalid user credentials for admin@example.test"}"""));
        using var client = CreateClient(handler, new List<TimeSpan>());

        var exception = await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Contains("401", exception.Message);
        Assert.Contains("invalid_grant", exception.Message);
        Assert.DoesNotContain("admin@example.test", exception.Message);
        Assert.Equal("invalid_grant", exception.ErrorCode);
    }

    [Fact]
    public async Task ReportsOperationAndStatusWhenTheErrorBodyCannotBeRead()
    {
        var handler = new StubHandler(request => IsTokenRequest(request)
            ? Json(HttpStatusCode.OK, TokenBody)
            : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ThrowingContent() });
        using var client = CreateClient(handler, new List<TimeSpan>());

        var exception = await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Contains("400", exception.Message);
        Assert.Contains("list users", exception.Message);
        Assert.Null(exception.ErrorCode);
    }

    [Fact]
    public async Task DropsAnErrorFieldThatIsNotAShortCode()
    {
        var handler = new StubHandler(request => IsTokenRequest(request)
            ? Json(HttpStatusCode.OK, TokenBody)
            : Json(HttpStatusCode.BadRequest, """{"error":"jdoe@example.test is invalid"}"""));
        using var client = CreateClient(handler, new List<TimeSpan>());

        var exception = await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Null(exception.ErrorCode);
        Assert.DoesNotContain("jdoe", exception.Message);
    }

    [Fact]
    public async Task RetriesATooManyRequestsAnswerAfterOneSecond()
    {
        var delays = new List<TimeSpan>();
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            return ++users == 1 ? Json(HttpStatusCode.TooManyRequests, "{}") : Json(HttpStatusCode.OK, "[]");
        });
        using var client = CreateClient(handler, delays);

        await client.LoadUsersAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
        Assert.Equal(2, users);
    }

    [Fact]
    public async Task GivesUpAfterFiveServerErrorsWithGrowingDelays()
    {
        var delays = new List<TimeSpan>();
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            users++;
            return Json(HttpStatusCode.ServiceUnavailable, "{}");
        });
        using var client = CreateClient(handler, delays);

        var exception = await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(5, users);
        Assert.Equal([1, 2, 4, 8], delays.Select(delay => (int)delay.TotalSeconds));
    }

    [Fact]
    public async Task HonoursARetryAfterHeader()
    {
        var delays = new List<TimeSpan>();
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            if (++users > 1)
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            var response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return response;
        });
        using var client = CreateClient(handler, delays);

        await client.LoadUsersAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task CapsARetryAfterHeaderAtThirtySeconds()
    {
        var delays = new List<TimeSpan>();
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            if (++users > 1)
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            var response = Json(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(600));
            return response;
        });
        using var client = CreateClient(handler, delays);

        await client.LoadUsersAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(30)], delays);
    }

    [Fact]
    public async Task DoesNotRetryAClientError()
    {
        var delays = new List<TimeSpan>();
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            users++;
            return Json(HttpStatusCode.BadRequest, "{}");
        });
        using var client = CreateClient(handler, delays);

        await Assert.ThrowsAsync<KeycloakAdminException>(() => client.LoadUsersAsync(CancellationToken.None));

        Assert.Equal(1, users);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task RetriesTheTokenRequest()
    {
        var delays = new List<TimeSpan>();
        var tokens = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                return ++tokens == 1 ? Json(HttpStatusCode.BadGateway, "{}") : Json(HttpStatusCode.OK, TokenBody);
            }

            return Json(HttpStatusCode.OK, "[]");
        });
        using var client = CreateClient(handler, delays);

        await client.LoadUsersAsync(CancellationToken.None);

        Assert.Equal(2, tokens);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task ResendsTheBodyOfARetriedPut()
    {
        var bodies = new List<string>();
        var handler = new StubHandler(async request =>
        {
            if (IsTokenRequest(request))
            {
                return Json(HttpStatusCode.OK, TokenBody);
            }

            bodies.Add(await request.Content!.ReadAsStringAsync());
            return bodies.Count == 1 ? Json(HttpStatusCode.InternalServerError, "{}") : Json(HttpStatusCode.NoContent, string.Empty);
        });
        using var client = CreateClient(handler, new List<TimeSpan>());

        await client.OpenRegistrationAsync(CancellationToken.None);

        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("registrationAllowed", bodies[1]);
    }

    [Fact]
    public async Task RefreshesTheTokenAfterAnUnauthorizedAnswer()
    {
        var tokens = 0;
        var users = 0;
        var handler = new StubHandler(request =>
        {
            if (IsTokenRequest(request))
            {
                tokens++;
                return Json(HttpStatusCode.OK, TokenBody);
            }

            return ++users == 1 ? Json(HttpStatusCode.Unauthorized, "{}") : Json(HttpStatusCode.OK, "[]");
        });
        using var client = CreateClient(handler, new List<TimeSpan>());

        await client.LoadUsersAsync(CancellationToken.None);

        Assert.Equal(2, tokens);
    }

    private static KeycloakAdminClient CreateClient(HttpMessageHandler handler, List<TimeSpan> delays) =>
        new(
            new KeycloakAdminSettings(new Uri("http://keycloak.test"), "cichlids", "stub-admin", "stub-password"),
            500,
            500,
            handler,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("The connection was closed.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
