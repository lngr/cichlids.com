using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The web app's session through the API (backend for frontend): the login runs the
/// authorization code flow with PKCE on the server, the browser receives only an opaque httpOnly
/// cookie, cookie-authenticated writes need the CSRF token, and the server renews the stored
/// tokens: a refusal ends the session, an unreachable Keycloak keeps it.
/// </summary>
[Collection(WebSessionCollection.Name)]
[Trait("Story", "task-3.41")]
public sealed class WebSessionTests(WebSessionFixture fixture)
{
    private const string SessionCookie = "__Host-cichlids-session";
    private const string LoginCookie = "__Host-cichlids-login";

    [Fact]
    public async Task Login_redirects_to_Keycloak_with_PKCE_and_the_offline_scope()
    {
        using var response = await fixture.Client.GetAsync("/api/auth/login?returnUrl=%2Fme");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(FakeKeycloak.AuthorizationEndpoint + "?", location.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("cichlids-app", query["client_id"].ToString());
        Assert.Equal(WebSessionFixture.CallbackUrl, query["redirect_uri"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"].ToString()));
        Assert.False(string.IsNullOrEmpty(query["state"].ToString()));
        Assert.Contains("offline_access", query["scope"].ToString().Split(' '));

        var cookie = SetCookie(response, LoginCookie);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
    }

    [Fact]
    public async Task Callback_exchanges_the_code_with_the_verifier_and_sets_only_an_opaque_session_cookie()
    {
        var login = await LogInAsync("/me");

        Assert.Equal("/me", login.CallbackLocation);
        var exchange = fixture.Keycloak.TokenRequests.Single(r => r.GetValueOrDefault("code") == login.Code);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal(WebSessionFixture.CallbackUrl, exchange["redirect_uri"]);
        Assert.Equal("cichlids-app", exchange["client_id"]);
        Assert.Equal(login.CodeChallenge, Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(exchange["code_verifier"]))));

        var cookie = login.Cookie;
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.Null(cookie.Domain.Value);
        Assert.True(cookie.MaxAge >= TimeSpan.FromDays(300), $"Max-Age is {cookie.MaxAge}");
        Assert.DoesNotContain(login.AccessToken, cookie.Value.ToString());
        Assert.DoesNotContain(login.RefreshToken, cookie.Value.ToString());

        await using var db = fixture.CreateDbContext();
        var stored = await db.WebSessions.SingleAsync(s => s.Subject == login.Subject);
        Assert.Equal(login.RefreshToken, stored.RefreshToken);
        Assert.NotEqual(Encoding.ASCII.GetBytes(cookie.Value.ToString()), stored.SessionIdHash);
    }

    [Fact]
    public async Task Session_endpoint_reports_the_session_with_a_CSRF_token_and_no_token()
    {
        var login = await LogInAsync("/");
        fixture.Keycloak.RenewWith(login.RefreshToken, new TokenAnswer(login.AccessToken, login.RefreshToken));

        using var response = await SendAsync(HttpMethod.Get, "/api/auth/session", login.CookieHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("csrfToken").GetString()));
        Assert.DoesNotContain(login.AccessToken, body);
        Assert.DoesNotContain(login.RefreshToken, body);
        Assert.True(SetCookie(response, SessionCookie).MaxAge >= TimeSpan.FromDays(300), "the session cookie is renewed");
    }

    [Fact]
    public async Task Session_endpoint_without_a_cookie_reports_no_session()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/auth/session", cookie: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task The_session_cookie_authenticates_API_calls()
    {
        var login = await LogInAsync("/");

        using var response = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(login.Username, json.RootElement.GetProperty("profile").GetProperty("username").GetString());
    }

    [Fact]
    public async Task A_cookie_authenticated_write_needs_the_CSRF_token_and_an_allowed_origin()
    {
        var login = await LogInAsync("/");
        var csrf = await CsrfTokenAsync(login);

        using (var missing = await SendAsync(HttpMethod.Post, "/api/uploads", login.CookieHeader, origin: WebSessionFixture.WebOrigin))
        {
            await AssertProblemAsync(missing, HttpStatusCode.Forbidden, "csrf_rejected");
        }

        using (var wrong = await SendAsync(HttpMethod.Post, "/api/uploads", login.CookieHeader, csrf: "not-the-token", origin: WebSessionFixture.WebOrigin))
        {
            await AssertProblemAsync(wrong, HttpStatusCode.Forbidden, "csrf_rejected");
        }

        using (var foreign = await SendAsync(HttpMethod.Post, "/api/uploads", login.CookieHeader, csrf, origin: "https://evil.example"))
        {
            await AssertProblemAsync(foreign, HttpStatusCode.Forbidden, "csrf_rejected");
        }

        // Authenticated and past the CSRF check, the upload fails for its missing file.
        using var accepted = await SendAsync(HttpMethod.Post, "/api/uploads", login.CookieHeader, csrf, origin: WebSessionFixture.WebOrigin);
        Assert.NotEqual(HttpStatusCode.Unauthorized, accepted.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, accepted.StatusCode);
        Assert.True((int)accepted.StatusCode is >= 400 and < 500, $"status {accepted.StatusCode}");
    }

    [Fact]
    public async Task A_bearer_token_takes_precedence_over_the_cookie_and_needs_no_CSRF_token()
    {
        var login = await LogInAsync("/");
        var bearerSubject = Guid.NewGuid().ToString();
        var bearerUsername = $"bearer-{bearerSubject[..8]}";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Cookie", login.CookieHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(bearerSubject, bearerUsername));
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(bearerUsername, json.RootElement.GetProperty("profile").GetProperty("username").GetString());
    }

    [Fact]
    public async Task Logout_ends_the_session_revokes_the_offline_token_and_clears_the_cookie()
    {
        var login = await LogInAsync("/");
        var csrf = await CsrfTokenAsync(login);

        using (var refused = await SendAsync(HttpMethod.Post, "/api/auth/logout", login.CookieHeader, origin: WebSessionFixture.WebOrigin))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Forbidden, "csrf_rejected");
        }

        using var response = await SendAsync(HttpMethod.Post, "/api/auth/logout", login.CookieHeader, csrf, WebSessionFixture.WebOrigin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(SetCookie(response, SessionCookie).Expires < DateTimeOffset.UtcNow, "the session cookie is cleared");
        Assert.Contains(fixture.Keycloak.LogoutRequests, form => form.GetValueOrDefault("refresh_token") == login.RefreshToken);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.False(await db.WebSessions.AnyAsync(s => s.Subject == login.Subject));
        }

        using var after = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task An_expired_access_token_is_renewed_on_the_server()
    {
        var login = await LogInAsync("/");
        await ExpireAccessTokenAsync(login);
        var renewedToken = TestTokens.Create(login.Subject, login.Username);
        fixture.Keycloak.RenewWith(login.RefreshToken, new TokenAnswer(renewedToken, login.RefreshToken));

        using var response = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(fixture.Keycloak.TokenRequests, form =>
            form.GetValueOrDefault("grant_type") == "refresh_token" && form.GetValueOrDefault("refresh_token") == login.RefreshToken);
        await using var db = fixture.CreateDbContext();
        var stored = await db.WebSessions.SingleAsync(s => s.Subject == login.Subject);
        Assert.Equal(renewedToken, stored.AccessToken);
        Assert.True(stored.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_refused_renewal_ends_the_session()
    {
        var login = await LogInAsync("/");
        await ExpireAccessTokenAsync(login);
        fixture.Keycloak.Refuse(login.RefreshToken);

        using var response = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(SetCookie(response, SessionCookie).Expires < DateTimeOffset.UtcNow, "the session cookie is cleared");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebSessions.AnyAsync(s => s.Subject == login.Subject));
    }

    [Fact]
    public async Task Opening_the_app_signs_out_a_suspended_account()
    {
        var login = await LogInAsync("/");
        fixture.Keycloak.Refuse(login.RefreshToken);

        using var response = await SendAsync(HttpMethod.Get, "/api/auth/session", login.CookieHeader);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebSessions.AnyAsync(s => s.Subject == login.Subject));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unreachable_Keycloak_keeps_the_session(bool serverError)
    {
        var login = await LogInAsync("/");
        await ExpireAccessTokenAsync(login);
        if (serverError)
        {
            fixture.Keycloak.FailWithServerError(login.RefreshToken);
        }
        else
        {
            fixture.Keycloak.BeUnreachableFor(login.RefreshToken);
        }

        using (var call = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader))
        {
            await AssertProblemAsync(call, HttpStatusCode.ServiceUnavailable, "identity_provider_unavailable");
        }

        using (var session = await SendAsync(HttpMethod.Get, "/api/auth/session", login.CookieHeader))
        {
            using var json = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        }

        fixture.Keycloak.RenewWith(login.RefreshToken, new TokenAnswer(TestTokens.Create(login.Subject, login.Username), login.RefreshToken));
        using var recovered = await SendAsync(HttpMethod.Get, "/api/me", login.CookieHeader);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task Login_refuses_a_return_address_outside_the_web_app(string returnUrl)
    {
        using var response = await fixture.Client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_accepts_an_absolute_return_address_of_the_web_app()
    {
        var login = await LogInAsync(WebSessionFixture.WebOrigin + "/gallery/abc");

        Assert.Equal(WebSessionFixture.WebOrigin + "/gallery/abc", login.CallbackLocation);
    }

    [Fact]
    public async Task Callback_with_a_foreign_state_creates_no_session()
    {
        var start = await StartLoginAsync("/me");
        fixture.Keycloak.IssueForCode("foreign-state-code", new TokenAnswer(TestTokens.Create(Guid.NewGuid().ToString(), "foreign"), "foreign-refresh"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/callback?code=foreign-state-code&state=forged");
        request.Headers.Add("Cookie", start.LoginCookieHeader);
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), cookie => cookie.Name == SessionCookie && cookie.Value.Length > 0);
    }

    [Fact]
    public async Task Callback_with_a_Keycloak_error_returns_to_the_app_without_a_session()
    {
        var start = await StartLoginAsync("/me");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/callback?error=access_denied&state={start.State}");
        request.Headers.Add("Cookie", start.LoginCookieHeader);
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/me", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(SetCookies(response), cookie => cookie.Name == SessionCookie && cookie.Value.Length > 0);
    }

    private sealed record LoginStart(string State, string CodeChallenge, string LoginCookieHeader);

    private sealed record Login(
        string Subject,
        string Username,
        string Code,
        string CodeChallenge,
        string AccessToken,
        string RefreshToken,
        string CallbackLocation,
        SetCookieHeaderValue Cookie)
    {
        public string CookieHeader => $"{Cookie.Name}={Cookie.Value}";
    }

    private async Task<LoginStart> StartLoginAsync(string returnUrl)
    {
        using var response = await fixture.Client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        var cookie = SetCookie(response, LoginCookie);
        return new LoginStart(query["state"].ToString(), query["code_challenge"].ToString(), $"{cookie.Name}={cookie.Value}");
    }

    private async Task<Login> LogInAsync(string returnUrl)
    {
        var start = await StartLoginAsync(returnUrl);
        var subject = Guid.NewGuid().ToString();
        var username = $"web-{subject[..8]}";
        var code = $"code-{Guid.NewGuid():N}";
        var accessToken = TestTokens.Create(subject, username);
        var refreshToken = $"offline-{Guid.NewGuid():N}";
        fixture.Keycloak.IssueForCode(code, new TokenAnswer(accessToken, refreshToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/callback?code={code}&state={Uri.EscapeDataString(start.State)}");
        request.Headers.Add("Cookie", start.LoginCookieHeader);
        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return new Login(
            subject,
            username,
            code,
            start.CodeChallenge,
            accessToken,
            refreshToken,
            response.Headers.Location!.OriginalString,
            SetCookie(response, SessionCookie));
    }

    private async Task<string> CsrfTokenAsync(Login login)
    {
        fixture.Keycloak.RenewWith(login.RefreshToken, new TokenAnswer(login.AccessToken, login.RefreshToken));
        using var response = await SendAsync(HttpMethod.Get, "/api/auth/session", login.CookieHeader);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private async Task ExpireAccessTokenAsync(Login login)
    {
        await using var db = fixture.CreateDbContext();
        await db.WebSessions
            .Where(s => s.Subject == login.Subject)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.AccessTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, string? csrf = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (csrf is not null)
        {
            request.Headers.Add("X-CSRF-Token", csrf);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return fixture.Client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList())
            : [];

    private static SetCookieHeaderValue SetCookie(HttpResponseMessage response, string name) =>
        SetCookies(response).Single(cookie => cookie.Name == name);
}
