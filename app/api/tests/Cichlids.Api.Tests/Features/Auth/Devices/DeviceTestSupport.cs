using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Cichlids.Api.Features.Auth.Devices;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cichlids.Api.Tests.Features.Auth.Devices;

/// <summary>
/// The signing keys of the device tokens of the test API, generated once per test run: a current
/// key and a previous key, so tests forge tokens with a key the API trusts or with a key it does
/// not know.
/// </summary>
internal static class DeviceTestKeys
{
    public static readonly string CurrentKeyId = "test-current";
    public static readonly string PreviousKeyId = "test-previous";

    private static readonly ECDsa Current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly ECDsa Previous = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The key set JSON the test API reads from DeviceTokens:KeySet.</summary>
    public static string KeySetJson { get; } = new DeviceTokenKeySet
    {
        Current = new DeviceTokenKeySet.CurrentKey
        {
            Kid = CurrentKeyId,
            PrivateKey = WebEncoders.Base64UrlEncode(Current.ExportPkcs8PrivateKey()),
        },
        Previous =
        [
            new DeviceTokenKeySet.PreviousKey
            {
                Kid = PreviousKeyId,
                PublicKey = WebEncoders.Base64UrlEncode(Previous.ExportSubjectPublicKeyInfo()),
            },
        ],
    }.ToJson();

    /// <summary>Signs a token like the API does, with the claims and settings the test chooses.</summary>
    public static string Forge(
        Guid credentialId,
        long profileId,
        string issuer = DeviceTokenSettings.DefaultIssuer,
        string audience = DeviceTokenSettings.DefaultAudience,
        TimeSpan? age = null,
        string? keyId = null,
        ECDsa? key = null,
        bool provisional = true)
    {
        var issuedAt = DateTime.UtcNow - (age ?? TimeSpan.Zero);
        var claims = new List<Claim>
        {
            new("sub", profileId.ToString()),
            new(CallerKind.CredentialClaim, credentialId.ToString("D")),
        };
        if (provisional)
        {
            claims.Add(new Claim(CallerKind.ProvisionalClaim, "true"));
        }

        var signingKey = new ECDsaSecurityKey(key ?? Current) { KeyId = keyId ?? CurrentKeyId };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + DeviceTokenSettings.TokenLifetime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256),
            Subject = new ClaimsIdentity(claims),
        });
    }

    public static ECDsa PreviousKey => Previous;
}

/// <summary>
/// An app installation as the tests play it: a P-256 key pair that registers, asks for challenges,
/// signs them and exchanges them for tokens through the HTTP API.
/// </summary>
internal sealed class TestDevice(HttpClient client) : IDisposable
{
    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Guid CredentialId { get; private set; }

    public long ProfileId { get; private set; }

    public string PublicKey => WebEncoders.Base64UrlEncode(Key.ExportSubjectPublicKeyInfo());

    public void Dispose() => Key.Dispose();

    public async Task RegisterAsync()
    {
        var response = await client.PostAsJsonAsync("/api/auth/device/credentials", new { publicKey = PublicKey });
        response.EnsureSuccessStatusCode();
        var credential = (await response.Content.ReadFromJsonAsync<DeviceCredentialDto>(TestJson.Options))!;
        CredentialId = credential.CredentialId;
        ProfileId = credential.ProfileId;
    }

    public async Task<DeviceChallengeDto> ChallengeAsync()
    {
        var response = await client.PostAsync("/api/auth/device/challenges", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceChallengeDto>(TestJson.Options))!;
    }

    public string Sign(string challenge, Guid credentialId, long timestamp, ECDsa? key = null) =>
        WebEncoders.Base64UrlEncode((key ?? Key).SignData(
            DeviceSignatureMessage.Build(challenge, credentialId, timestamp),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public Task<HttpResponseMessage> ExchangeAsync(string challenge, string signature, long timestamp, Guid? credentialId = null) =>
        client.PostAsJsonAsync("/api/auth/device/tokens", new
        {
            challenge,
            credentialId = credentialId ?? CredentialId,
            timestamp,
            signature,
        });

    /// <summary>Asks for a challenge, signs it and returns the response of the exchange.</summary>
    public async Task<HttpResponseMessage> SignInAsync()
    {
        var challenge = await ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await ExchangeAsync(challenge.Challenge, Sign(challenge.Challenge, CredentialId, timestamp), timestamp);
    }

    public async Task<string> TokenAsync()
    {
        var response = await SignInAsync();
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceTokenDto>(TestJson.Options))!.AccessToken;
    }

    public static HttpRequestMessage Get(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
