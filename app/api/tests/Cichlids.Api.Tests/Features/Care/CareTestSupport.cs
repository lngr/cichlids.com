using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Uploads;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Care;

/// <summary>
/// Request helpers shared by the tests of care notes, photos and to-dos: members with a tank,
/// JSON requests with a bearer token, coded problem assertions and outbox event reads.
/// </summary>
internal static class CareTestSupport
{
    /// <summary>
    /// A write service on the given context, for tests that act at a precise point of a save. Its
    /// object store never serves a request, since these tests write entries without photos.
    /// </summary>
    public static CareWriteService CreateWriteService(CichlidsDbContext context) => new(context, TimeProvider.System,
        new CareEntryMapper(new CarePhotoUrlSigner(DataProtectionProvider.Create("care-tests"))),
        new StoredObjectDeleter(new UrlOnlyObjectStore("http://objects.test"), NullLogger<StoredObjectDeleter>.Instance));

    /// <summary>
    /// Removes the given tanks with their care entries and outbox events, so tests that share the
    /// seeded database leave no tank behind.
    /// </summary>
    public static async Task RemoveTanksAsync(CichlidsDbContext db, IReadOnlyCollection<long> tanks)
    {
        var events = await db.OutboxEvents.Where(e => e.AggregateType == "care" || e.AggregateType == "tank" || e.AggregateType == "todo").ToListAsync();
        db.OutboxEvents.RemoveRange(events.Where(e =>
        {
            using var payload = JsonDocument.Parse(e.Payload);
            return payload.RootElement.TryGetProperty("tankId", out var id) && tanks.Contains(id.GetInt64());
        }));
        await db.SaveChangesAsync();
        await db.CareEntries.Where(e => tanks.Contains(e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => tanks.Contains(t.Id)).ExecuteDeleteAsync();
    }

    public static string NewMemberToken() => TestTokens.Create(Guid.NewGuid().ToString(), $"care-{Guid.NewGuid():N}"[..24]);

    public static async Task<(string Token, long Tank)> OwnTankAsync(HttpClient client)
    {
        var token = NewMemberToken();
        return (token, await CreateTankAsync(client, token));
    }

    public static async Task<long> CreateTankAsync(HttpClient client, string token)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/tanks", token, new { title = "Care test" });
        response.EnsureSuccessStatusCode();
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    public static async Task<long> LogAsync(HttpClient client, string token, long tank, object? body = null)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/tanks/{tank}/care", token, body ?? new { });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task AssertProblemAsync(HttpResponseMessage response, System.Net.HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await BodyAsync(response)).GetProperty("code").GetString());
    }

    public static async Task<List<(string Type, JsonElement Payload)>> EventsAsync(CichlidsDbContext db, string aggregate, long id)
    {
        var events = await db.OutboxEvents.AsNoTracking().Where(e => e.AggregateType == aggregate && e.AggregateId == id.ToString())
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync();
        return events.Select(e => (e.EventType, JsonDocument.Parse(e.Payload).RootElement.Clone())).ToList();
    }
}
