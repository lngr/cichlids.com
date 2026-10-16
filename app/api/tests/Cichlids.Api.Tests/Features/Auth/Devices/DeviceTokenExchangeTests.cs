// Story: task-7.1
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// The exchange of a signed one-time challenge for a device token: the token of a registered
/// installation, and the refusal of used or expired challenges, signatures of other keys or other
/// data, retired credentials and timestamps outside the window.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-7.1")]
public sealed class DeviceTokenExchangeTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_signed_challenge_yields_a_token_for_the_provisional_profile()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await device.SignInAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = (await response.Content.ReadFromJsonAsync<DeviceTokenDto>(TestJson.Options))!;
        Assert.Equal("Bearer", body.TokenType);
        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.AccessToken);
        Assert.Equal("ES256", token.Alg);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(DeviceTokenSettings.DefaultIssuer, token.Issuer);
        Assert.Contains(DeviceTokenSettings.DefaultAudience, token.Audiences);
        Assert.Equal(device.ProfileId.ToString(), token.Subject);
        Assert.Equal("true", token.GetClaim(CallerKind.ProvisionalClaim).Value);
        Assert.Equal(device.CredentialId.ToString("D"), token.GetClaim(CallerKind.CredentialClaim).Value);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
        Assert.InRange((body.ExpiresAt - new DateTimeOffset(token.ValidTo)).TotalSeconds, -1, 1);
        Assert.DoesNotContain(body.GetType().GetProperties(), p => p.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_token_authenticates_the_provisional_profile_at_me()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();

        var response = await fixture.Client.SendAsync(TestDevice.Get("/api/me", await device.TokenAsync()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("provisional").GetBoolean());
        Assert.Equal(device.ProfileId, body.RootElement.GetProperty("profile").GetProperty("id").GetInt64());
        Assert.Empty(body.RootElement.GetProperty("roles").EnumerateArray());
    }

    [Fact]
    public async Task Every_exchange_records_the_last_signed_request_time()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await using (var db = fixture.CreateDbContext())
        {
            await db.DeviceCredentials.Where(c => c.Id == device.CredentialId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSignedRequestAt, DateTimeOffset.UtcNow.AddDays(-30)));
        }

        var before = DateTimeOffset.UtcNow;
        (await device.SignInAsync()).EnsureSuccessStatusCode();

        await using var check = fixture.CreateDbContext();
        var last = await check.DeviceCredentials.Where(c => c.Id == device.CredentialId).Select(c => c.LastSignedRequestAt).SingleAsync();
        Assert.True(last >= before.AddSeconds(-1), last.ToString());
    }

    [Fact]
    public async Task A_refused_exchange_leaves_the_last_signed_request_time()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp, other), timestamp);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        var credential = await db.DeviceCredentials.SingleAsync(c => c.Id == device.CredentialId);
        Assert.Equal(credential.CreatedAt, credential.LastSignedRequestAt);
    }

    [Fact]
    public async Task A_challenge_is_accepted_once()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = device.Sign(challenge.Challenge, device.CredentialId, timestamp);

        var first = await device.ExchangeAsync(challenge.Challenge, signature, timestamp);
        var replay = await device.ExchangeAsync(challenge.Challenge, signature, timestamp);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertRefusedAsync(replay);
    }

    [Fact]
    public async Task Parallel_exchanges_of_one_challenge_succeed_exactly_once()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = device.Sign(challenge.Challenge, device.CredentialId, timestamp);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => device.ExchangeAsync(challenge.Challenge, signature, timestamp)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task An_expired_challenge_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        await using (var db = fixture.CreateDbContext())
        {
            await db.DeviceChallenges.ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp), timestamp);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task A_challenge_expires_after_two_minutes()
    {
        using var device = new TestDevice(fixture.Client);

        var challenge = await device.ChallengeAsync();

        var remaining = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        Assert.InRange(remaining, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(121));
    }

    [Fact]
    public async Task Only_the_hash_of_a_challenge_is_stored()
    {
        using var device = new TestDevice(fixture.Client);

        var challenge = await device.ChallengeAsync();

        var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(challenge.Challenge);
        Assert.Equal(32, bytes.Length);
        await using var db = fixture.CreateDbContext();
        var hash = SHA256.HashData(bytes);
        Assert.True(await db.DeviceChallenges.AnyAsync(c => c.ChallengeHash == hash));
        Assert.False(await db.DeviceChallenges.AnyAsync(c => c.ChallengeHash == bytes));
    }

    [Fact]
    public async Task A_challenge_that_was_never_issued_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await device.ExchangeAsync(challenge, device.Sign(challenge, device.CredentialId, timestamp), timestamp);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task A_signature_of_another_key_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp, attacker), timestamp);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task A_signature_over_other_data_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var challenge = await device.ChallengeAsync();
        var otherChallenge = await device.ChallengeAsync();
        await AssertRefusedAsync(await device.ExchangeAsync(
            challenge.Challenge, device.Sign(otherChallenge.Challenge, device.CredentialId, timestamp), timestamp));

        var third = await device.ChallengeAsync();
        await AssertRefusedAsync(await device.ExchangeAsync(
            third.Challenge, device.Sign(third.Challenge, Guid.NewGuid(), timestamp), timestamp));

        var fourth = await device.ChallengeAsync();
        await AssertRefusedAsync(await device.ExchangeAsync(
            fourth.Challenge, device.Sign(fourth.Challenge, device.CredentialId, timestamp - 1), timestamp));

        var fifth = await device.ChallengeAsync();
        var message = System.Text.Encoding.UTF8.GetBytes(fifth.Challenge);
        var bare = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            device.Key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        await AssertRefusedAsync(await device.ExchangeAsync(fifth.Challenge, bare, timestamp));
    }

    [Fact]
    public async Task A_signature_in_the_der_format_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var der = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(device.Key.SignData(
            DeviceSignatureMessage.Build(challenge.Challenge, device.CredentialId, timestamp),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        await AssertRefusedAsync(await device.ExchangeAsync(challenge.Challenge, der, timestamp));
    }

    [Fact]
    public async Task A_valid_signature_of_one_device_does_not_work_for_the_credential_of_another()
    {
        using var device = new TestDevice(fixture.Client);
        using var victim = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await victim.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await device.ExchangeAsync(
            challenge.Challenge, device.Sign(challenge.Challenge, victim.CredentialId, timestamp), timestamp, victim.CredentialId);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task An_unknown_credential_is_refused()
    {
        using var device = new TestDevice(fixture.Client);
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var unknown = Guid.NewGuid();

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, unknown, timestamp), timestamp, unknown);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task A_retired_credential_gets_no_token()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        await RetireAsync(device.CredentialId);

        await AssertRefusedAsync(await device.SignInAsync());
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(-121)]
    [InlineData(121)]
    [InlineData(3600)]
    public async Task A_timestamp_outside_the_window_is_refused(int offsetSeconds)
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offsetSeconds;

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp), timestamp);

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task A_timestamp_just_inside_the_window_is_accepted()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 90;

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp), timestamp);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_attempt_with_the_wrong_timestamp_does_not_use_the_challenge()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var challenge = await device.ChallengeAsync();
        var late = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        await AssertRefusedAsync(await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, late), late));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, now), now);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_signature_uses_up_the_challenge()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await AssertRefusedAsync(await device.ExchangeAsync(
            challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp, attacker), timestamp));

        var response = await device.ExchangeAsync(challenge.Challenge, device.Sign(challenge.Challenge, device.CredentialId, timestamp), timestamp);

        await AssertRefusedAsync(response);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"challenge":"AAAA","credentialId":"00000000-0000-0000-0000-000000000000","timestamp":1,"signature":"AAAA"}""")]
    [InlineData("""{"challenge":"!!","credentialId":"00000000-0000-0000-0000-000000000001","timestamp":1,"signature":"!!"}""")]
    public async Task A_malformed_request_is_refused_with_the_same_answer(string json)
    {
        var response = await fixture.Client.PostAsync(
            "/api/auth/device/tokens", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        await AssertRefusedAsync(response);
    }

    [Fact]
    public async Task An_unknown_credential_and_a_replay_have_the_same_status_and_code()
    {
        using var device = new TestDevice(fixture.Client);
        await device.RegisterAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var unknownCredential = Guid.NewGuid();

        var first = await device.ChallengeAsync();
        var unknown = await device.ExchangeAsync(first.Challenge, device.Sign(first.Challenge, unknownCredential, timestamp), timestamp, unknownCredential);
        var second = await device.ChallengeAsync();
        var signature = device.Sign(second.Challenge, device.CredentialId, timestamp);
        var accepted = await device.ExchangeAsync(second.Challenge, signature, timestamp);
        var replay = await device.ExchangeAsync(second.Challenge, signature, timestamp);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await AssertRefusedAsync(unknown);
        await AssertRefusedAsync(replay);
        Assert.Equal(DetailOf(await unknown.Content.ReadAsStringAsync()), DetailOf(await replay.Content.ReadAsStringAsync()));
    }

    private async Task RetireAsync(Guid credentialId)
    {
        await using var db = fixture.CreateDbContext();
        await db.DeviceCredentials.Where(c => c.Id == credentialId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.State, DeviceCredentialState.Retired)
            .SetProperty(c => c.RetiredAt, DateTimeOffset.UtcNow));
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(DeviceAuthEndpoints.RefusedCode, CodeOf(await response.Content.ReadAsStringAsync()));
    }

    private static string? DetailOf(string json)
    {
        using var body = System.Text.Json.JsonDocument.Parse(json);
        return body.RootElement.GetProperty("detail").GetString();
    }

    private static string? CodeOf(string json)
    {
        using var body = System.Text.Json.JsonDocument.Parse(json);
        return body.RootElement.GetProperty("code").GetString();
    }
}
