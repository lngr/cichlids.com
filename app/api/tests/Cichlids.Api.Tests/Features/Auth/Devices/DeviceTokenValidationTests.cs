// Story: task-7.1
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// How the API validates device tokens next to Keycloak tokens: issuer, audience, lifetime, key id
/// and algorithm, retired credentials, the rotation of the signing key, and the flows a device
/// token must not reach (account creation, web session, administration, member audiences).
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-7.1")]
public sealed class DeviceTokenValidationTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_token_older_than_fifteen_minutes_is_refused_and_a_new_signature_yields_a_new_token()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var expired = DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, age: TimeSpan.FromMinutes(16));

        var refused = await GetMeAsync(expired);
        var fresh = await GetMeAsync(await device.TokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task A_token_younger_than_fifteen_minutes_is_accepted()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, age: TimeSpan.FromMinutes(14)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_of_another_issuer_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, issuer: "https://other.example.test"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, audience: "cichlids-api"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_an_unknown_key_id_is_refused_even_when_the_current_key_signed_it()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, keyId: "forged"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_by_a_foreign_key_under_the_current_key_id_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        using var foreign = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, key: foreign));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_by_the_previous_key_is_accepted_after_a_rotation()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(
            device.CredentialId, device.ProfileId, keyId: DeviceTestKeys.PreviousKeyId, key: DeviceTestKeys.PreviousKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_previous_key_id_with_the_current_key_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, keyId: DeviceTestKeys.PreviousKeyId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unsigned_token_of_the_device_issuer_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var header = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var payload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $$"""{"iss":"{{DeviceTokenSettings.DefaultIssuer}}","aud":"{{DeviceTokenSettings.DefaultAudience}}","sub":"{{device.ProfileId}}","provisional":"true","cid":"{{device.CredentialId}}","exp":{{exp}}}"""));

        var response = await GetMeAsync($"{header}.{payload}.");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_symmetric_token_of_the_device_issuer_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = DeviceTestKeys.CurrentKeyId };
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DeviceTokenSettings.DefaultIssuer,
            Audience = DeviceTokenSettings.DefaultAudience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", device.ProfileId.ToString()), new Claim("provisional", "true"), new Claim("cid", device.CredentialId.ToString())]),
        });

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_the_provisional_claim_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, device.ProfileId, provisional: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_the_profile_of_another_device_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        using var other = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await other.RegisterAsync();

        var response = await GetMeAsync(DeviceTestKeys.Forge(device.CredentialId, other.ProfileId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_of_a_retired_credential_is_refused_at_once()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var token = await device.TokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(token)).StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            await db.DeviceCredentials.Where(c => c.Id == device.CredentialId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.State, DeviceCredentialState.Retired)
                .SetProperty(c => c.RetiredAt, DateTimeOffset.UtcNow));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(token)).StatusCode);
    }

    [Fact]
    public async Task A_keycloak_token_still_works_and_is_registered()
    {
        var token = TestTokens.Create($"kc-{Guid.NewGuid():N}", $"kc-{Guid.NewGuid():N}");

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("provisional").GetBoolean());
    }

    [Fact]
    public async Task A_keycloak_token_does_not_pass_as_a_device_token_with_the_device_issuer()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DeviceTokenSettings.DefaultIssuer,
            Audience = TestTokens.Audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", "1"), new Claim("preferred_username", "intruder")]),
        });

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_device_token_creates_no_member_profile_and_no_account_link()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var token = await device.TokenAsync();
        await using var db = fixture.CreateDbContext();
        var profiles = await db.Profiles.CountAsync();
        var identities = await db.ProfileIdentities.CountAsync();

        var response = await fixture.Client.SendAsync(TestDevice.Get("/api/me/settings", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(RegistrationRequiredExceptionHandler.Code, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(profiles, await db.Profiles.CountAsync());
        Assert.Equal(identities, await db.ProfileIdentities.CountAsync());
    }

    [Theory]
    [InlineData("/api/me/drafts")]
    [InlineData("/api/me/legacy-tanks")]
    [InlineData("/api/me/vacations")]
    public async Task Endpoints_for_members_refuse_a_device_token_without_creating_anything(string url)
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await using var db = fixture.CreateDbContext();
        var profiles = await db.Profiles.CountAsync();

        var response = await fixture.Client.SendAsync(TestDevice.Get(url, await device.TokenAsync()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(profiles, await db.Profiles.CountAsync());
    }

    [Fact]
    public async Task A_device_token_does_not_open_a_web_session_or_the_administration()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var token = await device.TokenAsync();

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Authorization = new("Bearer", token);
        var logoutResponse = await fixture.Client.SendAsync(logout);
        var session = await fixture.Client.SendAsync(TestDevice.Get("/api/auth/session", token));
        var webhooks = await fixture.Client.SendAsync(TestDevice.Get("/api/admin/webhooks", token));
        var outbox = await fixture.Client.SendAsync(TestDevice.Get("/api/admin/outbox/events", token));

        Assert.Equal(HttpStatusCode.Unauthorized, logoutResponse.StatusCode);
        using var sessionBody = System.Text.Json.JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.False(sessionBody.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, webhooks.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, outbox.StatusCode);
    }

    [Fact]
    public async Task A_provisional_caller_is_anonymous_for_the_audiences_of_members()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        using var scope = fixture.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TankAccessService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", device.ProfileId.ToString()), new Claim("provisional", "true"), new Claim("cid", device.CredentialId.ToString())],
            DeviceTokenAuthentication.SchemeName));

        var viewer = await access.ViewerProfileIdAsync(principal, CancellationToken.None);

        Assert.Null(viewer);
        Assert.True(principal.IsProvisional());
        Assert.False(principal.IsRegistered());
        Assert.Null(principal.KeycloakSubject());
        Assert.Equal(device.ProfileId, principal.ProvisionalProfileId());
    }

    [Fact]
    public async Task A_device_token_reads_public_lists_like_a_visitor()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var token = await device.TokenAsync();

        var visitor = await (await fixture.Client.GetAsync("/api/tanks")).Content.ReadAsStringAsync();
        var provisional = await (await fixture.Client.SendAsync(TestDevice.Get("/api/tanks", token))).Content.ReadAsStringAsync();

        Assert.Equal(visitor, provisional);
    }

    [Fact]
    public void The_helpers_tell_anonymous_registered_and_provisional_callers_apart()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var registered = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "abc")], "Bearer"));
        var provisional = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "7"), new Claim("provisional", "true")], "DeviceBearer"));

        Assert.False(anonymous.IsRegistered());
        Assert.False(anonymous.IsProvisional());
        Assert.True(registered.IsRegistered());
        Assert.Equal("abc", registered.KeycloakSubject());
        Assert.Null(registered.ProvisionalProfileId());
        Assert.True(provisional.IsProvisional());
        Assert.False(provisional.IsRegistered());
        Assert.Null(provisional.KeycloakSubject());
        Assert.Equal(7, provisional.ProvisionalProfileId());
    }

    private Task<HttpResponseMessage> GetMeAsync(string token) => fixture.Client.SendAsync(TestDevice.Get("/api/me", token));
}
