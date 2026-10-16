// Story: task-7.1
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// Registering the public key of an installation: the provisional profile with a generated name and
/// no public handle, the validation of the key and the idempotent registration of the same key.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-7.1")]
public sealed partial class DeviceRegistrationTests(ApiFixture fixture)
{
    [GeneratedRegex("^user[1-9][0-9]{7}$")]
    private static partial Regex GeneratedName();

    [Fact]
    public async Task A_new_key_gets_a_provisional_profile_with_a_generated_name_and_an_active_credential()
    {
        using var device = new TestDevice(fixture.Client);

        var response = await fixture.Client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var credential = (await response.Content.ReadFromJsonAsync<DeviceCredentialDto>(TestJson.Options))!;
        Assert.Matches(GeneratedName(), credential.DisplayName);
        await using var db = fixture.CreateDbContext();
        var profile = await db.Profiles.SingleAsync(p => p.Id == credential.ProfileId);
        Assert.Equal(ProfileKind.Provisional, profile.Kind);
        Assert.Equal(credential.DisplayName, profile.DisplayName);
        Assert.Empty(await db.ProfileIdentities.Where(i => i.ProfileId == profile.Id).ToListAsync());
        var stored = await db.DeviceCredentials.SingleAsync(c => c.Id == credential.CredentialId);
        Assert.Equal(DeviceCredentialState.Active, stored.State);
        Assert.Equal("ES256", stored.Algorithm);
        Assert.Equal(profile.Id, stored.ProfileId);
        Assert.Equal(stored.CreatedAt, stored.LastSignedRequestAt);
    }

    [Fact]
    public async Task The_provisional_profile_is_not_public()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await fixture.Client.GetAsync($"/api/profiles/{device.ProfileId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_key_again_returns_the_same_credential()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var again = await fixture.Client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var credential = (await again.Content.ReadFromJsonAsync<DeviceCredentialDto>(TestJson.Options))!;
        Assert.Equal(device.CredentialId, credential.CredentialId);
        Assert.Equal(device.ProfileId, credential.ProfileId);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.DeviceCredentials.CountAsync(c => c.ProfileId == device.ProfileId));
    }

    [Fact]
    public async Task A_retired_key_cannot_register_again()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await RetireAsync(device.CredentialId);

        var response = await fixture.Client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DeviceAuthEndpoints.KeyRetiredCode, await CodeAsync(response));
    }

    [Fact]
    public async Task Parallel_registrations_of_one_key_create_one_profile()
    {
        using var device = new TestDevice(fixture.Client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            fixture.Client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey })));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        var credentials = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<DeviceCredentialDto>(TestJson.Options)));
        Assert.Single(credentials.Select(c => c!.CredentialId).Distinct());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.DeviceCredentials.CountAsync(c => c.Id == credentials[0]!.CredentialId));
        Assert.Equal(1, await db.Profiles.CountAsync(p => p.Id == credentials[0]!.ProfileId));
    }

    [Fact]
    public async Task Two_keys_get_two_profiles_with_different_names()
    {
        using var first = new TestDevice(fixture.Client);
        using var second = new TestDevice(fixture.Client);
        await first.RegisterAsync();
        await second.RegisterAsync();

        Assert.NotEqual(first.ProfileId, second.ProfileId);
        await using var db = fixture.CreateDbContext();
        var names = await db.Profiles.Where(p => p.Id == first.ProfileId || p.Id == second.ProfileId).Select(p => p.DisplayName).ToListAsync();
        Assert.Equal(2, names.Distinct().Count());
    }

    [Fact]
    public async Task A_key_on_another_curve_is_refused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var response = await RegisterRawAsync(WebEncoders.Base64UrlEncode(p384.ExportSubjectPublicKeyInfo()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DeviceAuthEndpoints.KeyInvalidCode, await CodeAsync(response));
    }

    [Fact]
    public async Task An_rsa_key_is_refused()
    {
        using var rsa = RSA.Create(2048);

        var response = await RegisterRawAsync(WebEncoders.Base64UrlEncode(rsa.ExportSubjectPublicKeyInfo()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_private_key_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await RegisterRawAsync(WebEncoders.Base64UrlEncode(key.ExportPkcs8PrivateKey()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_key_with_trailing_bytes_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await RegisterRawAsync(WebEncoders.Base64UrlEncode([.. key.ExportSubjectPublicKeyInfo(), 0]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("AAAA")]
    public async Task A_missing_or_unreadable_key_is_refused(string? publicKey)
    {
        var response = await RegisterRawAsync(publicKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_without_a_body_is_refused()
    {
        var response = await fixture.Client.PostAsync("/api/auth/device/credentials", null);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Registration_needs_no_authentication_and_ignores_an_unreadable_token()
    {
        using var device = new TestDevice(fixture.Client);
        using var anonymous = fixture.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "garbage");

        var response = await anonymous.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = device.PublicKey });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> RegisterRawAsync(string? publicKey) =>
        fixture.Client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey });

    private async Task RetireAsync(Guid credentialId)
    {
        await using var db = fixture.CreateDbContext();
        await db.DeviceCredentials.Where(c => c.Id == credentialId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.State, DeviceCredentialState.Retired)
            .SetProperty(c => c.RetiredAt, DateTimeOffset.UtcNow));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}
