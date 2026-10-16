using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Tests.Features.Auth.Devices;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// A Keycloak account as the tests play it: a token for a new subject.
/// </summary>
internal sealed record KeycloakAccount(string Subject, string Username, string Token)
{
    public static KeycloakAccount New(string? username = null, string? name = null)
    {
        var subject = Guid.NewGuid().ToString();
        username ??= $"reg-{Guid.NewGuid():N}"[..20];
        return new KeycloakAccount(subject, username, TestTokens.Create(subject, username, name));
    }
}

/// <summary>
/// Requests of the registration routes and the content a provisional profile holds in the tests of
/// the upgrade and the move.
/// </summary>
internal static class RegistrationTestSupport
{
    public const string Preview = "/api/me/provisional/preview?date=2026-10-15";
    public const string Upgrade = "/api/me/provisional/upgrade";
    public const string Move = "/api/me/provisional/move?date=2026-10-15";

    /// <summary>A fresh challenge signed by the device, as the body of a registration request.</summary>
    public static async Task<object> ProofAsync(TestDevice device)
    {
        var challenge = await device.ChallengeAsync();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new
        {
            challenge = challenge.Challenge,
            credentialId = device.CredentialId,
            timestamp,
            signature = device.Sign(challenge.Challenge, device.CredentialId, timestamp),
        };
    }

    public static async Task<HttpResponseMessage> PostWithProofAsync(HttpClient client, string route, string? token, TestDevice device)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(await ProofAsync(device)) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    public static async Task AssertDeviceAuthRefusedAsync(HttpResponseMessage response) =>
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "device_auth_refused");

    /// <summary>The content a provisional profile holds in the tests: a tank with care, a to-do and a vacation.</summary>
    internal sealed record Content(long Tank, long CareEntry, long MediaItem, long Todo);

    public static async Task<Content> AddContentAsync(ApiFixture fixture, ProvisionalCaller caller, string title = "Provisional tank")
    {
        var tank = await CreateTankAsync(fixture.Client, caller, new { title });
        var careEntry = await LogAsync(fixture.Client, caller.Token, tank, new { note = "first water change" });
        using var todo = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", caller.Token, new { text = "Order filter wool" });
        Assert.Equal(HttpStatusCode.Created, todo.StatusCode);
        var todoId = (await BodyAsync(todo)).GetProperty("id").GetInt64();

        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            OwnerProfileId = caller.ProfileId,
            Kind = MediaKind.Photo,
            StorageKey = $"originals/registration/{Guid.NewGuid():N}.jpg",
            ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForCareEntry(careEntry, item.Id, MediaLinkRole.Photo));
        await db.SaveChangesAsync();
        return new Content(tank, careEntry, item.Id, todoId);
    }

    public static async Task AddVacationAsync(ApiFixture fixture, string token, string start, string? end)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/me/vacations?date=2026-10-15", token, new { startDate = start, endDate = end });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The Keycloak account signs in for the first time and gets its member profile.</summary>
    public static async Task<long> MemberProfileIdAsync(ApiFixture fixture, KeycloakAccount account)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", account.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("profile").GetProperty("id").GetInt64();
    }

    public static async Task<List<JsonElement>> EventPayloadsAsync(CichlidsDbContext db, string type, long profileId) =>
        (await db.OutboxEvents.AsNoTracking().Where(e => e.EventType == type && e.AggregateId == profileId.ToString()).ToListAsync())
            .Select(e => JsonDocument.Parse(e.Payload).RootElement.Clone()).ToList();
}
