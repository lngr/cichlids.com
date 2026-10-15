using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Onboarding;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cichlids.Api.Tests.Features.Me;

[Collection(ApiCollection.Name)]
public sealed class OnboardingTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly string titlePrefix = $"Onboarding {Guid.NewGuid():N} ";
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Tanks.Where(t => t.Title.StartsWith(titlePrefix)).ExecuteDeleteAsync();
    }

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
            tank = new { title = titlePrefix + "First Malawi", region = "lake_malawi", dimensions = new { width = 60, height = 30, depth = 30, unit = "cm" }, volume = new { value = 54, unit = "liter" } },
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
        var response = await SendAsync("/api/me/onboarding", new { tank = new { title = titlePrefix + "Region", region } }, token);
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
    public async Task HiddenLegacyTankIsNeitherListedNorPublishedByTakeover()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"hidden-{Guid.NewGuid():N}"[..24]);
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var me = JsonDocument.Parse(await (await fixture.Client.SendAsync(read)).Content.ReadAsStringAsync());
        var profileId = me.RootElement.GetProperty("profile").GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { LegacyId = 987655, ProfileId = profileId, Title = titlePrefix + "Hidden", Category = TankCategory.Malawi, State = TankState.Draft, CreatedAt = DateTimeOffset.UtcNow };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/me/legacy-tanks");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listed = JsonDocument.Parse(await (await fixture.Client.SendAsync(list)).Content.ReadAsStringAsync());
        Assert.Equal(0, listed.RootElement.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("/api/me/onboarding", new { legacyTankId = tank.Id }, token)).StatusCode);
        Assert.Equal(TankState.Draft, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank.Id)).State);
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

    [Fact]
    public async Task ProfileOutboxFailureRollsBackTankBadgeInterestsAndCompletion()
    {
        await using var db = fixture.CreateDbContext();
        var profile = new Profile { Username = $"rollback-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow, Interests = ["lake_victoria"] };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        var title = titlePrefix + "Rollback";
        try
        {
            await using (var write = fixture.CreateDbContext(new RefuseCompletionEvent()))
            {
                var service = new OnboardingWriteService(write, new TanksWriteService(write));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(profile.Id,
                    new OnboardingRequest(Tank: new CreateTankRequest(title, null, null, TankRegion.LakeMalawi)), CancellationToken.None));
            }
            Assert.False(await db.Tanks.AnyAsync(t => t.Title == title));
            await db.Entry(profile).ReloadAsync();
            Assert.Null(profile.OnboardingCompletedAt);
            Assert.Empty(profile.Badges);
            Assert.Equal(new[] { "lake_victoria" }, profile.Interests);
            Assert.False(await db.OutboxEvents.AnyAsync(e => e.AggregateType == "profile" && e.AggregateId == profile.Id.ToString()));
        }
        finally { await db.Profiles.Where(p => p.Id == profile.Id).ExecuteDeleteAsync(); }
    }

    private sealed class RefuseCompletionEvent : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxEvent>().Any(e => e.State == EntityState.Added && e.Entity.EventType == "profile.onboarding_completed"))
                throw new InvalidOperationException("Completion event refused by test.");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task RegionRefinementRequiresOwnerAndAmericanRegionAndSavesAnEvent()
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"refine-{Guid.NewGuid():N}"[..24]);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var me = JsonDocument.Parse(await (await fixture.Client.SendAsync(request)).Content.ReadAsStringAsync());
        var profileId = me.RootElement.GetProperty("profile").GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var tank = new Tank { ProfileId = profileId, Title = titlePrefix + "Refine", Region = TankRegion.AmericasUnassigned, State = TankState.Published, CreatedAt = DateTimeOffset.UtcNow };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var foreign = TestTokens.Create(Guid.NewGuid().ToString(), $"foreign-{Guid.NewGuid():N}"[..24]);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync($"/api/tanks/{tank.Id}/region", new { region = "south_america" }, foreign, HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync($"/api/tanks/{tank.Id}/region", new { region = "lake_malawi" }, token, HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync($"/api/tanks/{tank.Id}/region", new { region = "central_america" }, token, HttpMethod.Put)).StatusCode);
        await db.Entry(tank).ReloadAsync();
        Assert.Equal(TankRegion.CentralAmerica, tank.Region);
        Assert.True(await db.OutboxEvents.AnyAsync(e => e.EventType == "tank.region_refined" && e.AggregateId == tank.Id.ToString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync($"/api/tanks/{tank.Id}/region", new { region = "south_america" }, token, HttpMethod.Put)).StatusCode);
    }
}
