using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// Members with a token, tanks at a chosen visibility and request helpers shared by the tests of the
/// tank visibility rules.
/// </summary>
internal static class TankVisibilityTestSupport
{
    public sealed record Member(long ProfileId, string Token);

    public static async Task<Member> NewMemberAsync(ApiFixture fixture)
    {
        var subject = Guid.NewGuid().ToString();
        var username = $"vis-{Guid.NewGuid():N}"[..24];
        await using var db = fixture.CreateDbContext();
        var profile = new Profile
        {
            Username = username, DisplayName = username, Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow,
            Identities = [new ProfileIdentity { Provider = "oidc", Subject = subject, CreatedAt = DateTimeOffset.UtcNow }],
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return new Member(profile.Id, TestTokens.Create(subject, username));
    }

    /// <summary>
    /// Inserts a published tank of the owner at the given levels, bypassing the creation endpoint.
    /// </summary>
    public static async Task<long> NewTankAsync(
        ApiFixture fixture, long ownerProfileId, TankVisibility profile, TankVisibility stream, string? title = null)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var tank = new Tank
        {
            ProfileId = ownerProfileId, Title = title ?? $"Visibility {Guid.NewGuid():N}", State = TankState.Published,
            CreatedAt = now, PublishedAt = now, ProfileVisibility = profile, StreamVisibility = stream,
            MediaPlacedPublic = TankVisibilityRulesFor(profile),
        };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        return tank.Id;
    }

    private static bool TankVisibilityRulesFor(TankVisibility profile) => Cichlids.Domain.TankVisibilityRules.MediaBelongsInPublicStorage(profile);

    public static async Task<HttpResponseMessage> SendAsync(
        ApiFixture fixture, HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await fixture.Client.SendAsync(request);
    }

    public static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task AssertProblemAsync(HttpResponseMessage response, System.Net.HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await BodyAsync(response)).GetProperty("code").GetString());
    }

    public static async Task RemoveTanksAsync(ApiFixture fixture, params long[] tanks)
    {
        await using var db = fixture.CreateDbContext();
        var ids = tanks.Select(t => t.ToString()).ToList();
        await db.OutboxEvents.Where(e => e.AggregateType == "tank" && ids.Contains(e.AggregateId)).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => tanks.Contains(e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => tanks.Contains(t.Id)).ExecuteDeleteAsync();
    }
}
