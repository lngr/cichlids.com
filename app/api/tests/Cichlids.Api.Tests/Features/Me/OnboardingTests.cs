using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Me;

[Collection(ApiCollection.Name)]
public sealed class OnboardingTests(ApiFixture fixture)
{
    private async Task<HttpResponseMessage> SendAsync(string path, object body, string token, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task StandardTankCompletesOnboardingWithInterestsBadgeAndTransactionalEvents()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"onboard-{Guid.NewGuid():N}"[..24]);
        var response = await SendAsync("/api/me/onboarding", new
        {
            tank = new { title = "First Malawi", region = "lake_malawi", dimensions = new { width = 60, height = 30, depth = 30, unit = "cm" }, volume = new { value = 54, unit = "liter" } },
        }, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tankId = json.RootElement.GetProperty("tankId").GetInt64();
        Assert.Equal("first_tank", json.RootElement.GetProperty("badge").GetString());
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.SingleAsync(t => t.Id == tankId);
        Assert.Equal(60m, tank.WidthValue);
        var profile = await db.Profiles.SingleAsync(p => p.Id == tank.ProfileId);
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await fixture.Client.SendAsync(read);
        using var detail = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var p = detail.RootElement.GetProperty("profile");
        Assert.NotEqual(JsonValueKind.Null, p.GetProperty("onboardingCompletedAt").ValueKind);
        Assert.Equal("lake_malawi", p.GetProperty("interests")[0].GetString());
        Assert.Equal("first_tank", p.GetProperty("badges")[0].GetString());
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "tank.created" && e.AggregateId == tankId.ToString()));
    }

    [Theory]
    [InlineData("mixed", 0)]
    [InlineData("lake_victoria", 1)]
    public async Task RegionSetsInterests(string region, int count)
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"region-{Guid.NewGuid():N}"[..24]);
        var response = await SendAsync("/api/me/onboarding", new { tank = new { title = "Region", region } }, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var json = JsonDocument.Parse(await (await fixture.Client.SendAsync(read)).Content.ReadAsStringAsync());
        Assert.Equal(count, json.RootElement.GetProperty("profile").GetProperty("interests").GetArrayLength());
    }

    [Fact]
    public async Task LegacyTakeoverRequiresOwnershipAndRefinementAndPreservesTank()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"legacy-{Guid.NewGuid():N}"[..24]);
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var me = JsonDocument.Parse(await (await fixture.Client.SendAsync(read)).Content.ReadAsStringAsync());
        var profileId = me.RootElement.GetProperty("profile").GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { LegacyId = 987654, ProfileId = profileId, Title = "Archive", Category = TankCategory.American, State = TankState.Published, CreatedAt = DateTimeOffset.UtcNow };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        try
        {
            var foreign = TestTokens.Create(Guid.NewGuid().ToString(), $"other-{Guid.NewGuid():N}"[..24]);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("/api/me/onboarding", new { legacyTankId = tank.Id, region = "south_america" }, foreign)).StatusCode);
            var result = await SendAsync("/api/me/onboarding", new { legacyTankId = tank.Id, region = "south_america" }, token);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            Assert.Equal(tank.Id, json.RootElement.GetProperty("tankId").GetInt64());
            Assert.Equal("returned", json.RootElement.GetProperty("badge").GetString());
            using var detail = JsonDocument.Parse(await (await fixture.Client.GetAsync($"/api/tanks/{tank.Id}")).Content.ReadAsStringAsync());
            Assert.Equal("south_america", detail.RootElement.GetProperty("region").GetString());
        }
        finally { await db.Tanks.Where(t => t.Id == tank.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task RejectsUnassignedForNewTankAndInvalidInterests()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"invalid-{Guid.NewGuid():N}"[..24]);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("/api/tanks", new { title = "Invalid", region = "americas_unassigned" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("/api/me/onboarding", new { interests = new[] { "americas_unassigned" } }, token)).StatusCode);
    }

    [Fact]
    public async Task CanSkipAndEditInterestsAsASet()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"skip-{Guid.NewGuid():N}"[..24]);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("/api/me/onboarding", new { skip = true }, token)).StatusCode);
        var response = await SendAsync("/api/me/interests", new { interests = new[] { "lake_malawi", "lake_malawi", "central_america" } }, token, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetArrayLength());
    }
}
