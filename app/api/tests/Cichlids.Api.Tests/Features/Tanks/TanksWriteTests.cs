using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public sealed class TanksWriteTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenValidTank_WhenCreated_ThenTankAndOutboxEventArePersisted()
    {
        var title = $"Outbox {Guid.NewGuid():N}";
        using var response = await CreateAsync(new { title = $"  {title}  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.SingleAsync(t => t.Id == id);
        Assert.Equal(title, tank.Title);
        var evt = Assert.Single(await db.OutboxEvents.Where(e => e.EventType == "tank.created" && e.AggregateId == id.ToString()).ToListAsync());
        Assert.Equal("tank", evt.AggregateType);
        Assert.Equal(tank.CreatedAt, evt.OccurredAt);
        Assert.Null(evt.DispatchedAt);
        using var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal(id, payload.RootElement.GetProperty("tankId").GetInt64());
        Assert.Equal(tank.ProfileId, payload.RootElement.GetProperty("profileId").GetInt64());
        await db.OutboxEvents.Where(e => e.Id == evt.Id).ExecuteDeleteAsync();
        await db.Tanks.Where(t => t.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GivenOutboxSaveFails_WhenCreatingTank_ThenTankInsertRollsBack()
    {
        var title = $"Rollback {Guid.NewGuid():N}";
        await using var read = fixture.CreateDbContext();
        var profileId = await read.Profiles.Select(p => p.Id).FirstAsync();
        var eventsBefore = await read.OutboxEvents.CountAsync();
        await using (var write = fixture.CreateDbContext(new RefuseOutboxSave()))
        {
            var service = new TanksWriteService(write);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
                new CreateTankRequest(title, null, null), profileId, CancellationToken.None));
        }
        Assert.False(await read.Tanks.AnyAsync(t => t.Title == title));
        Assert.Equal(eventsBefore, await read.OutboxEvents.CountAsync());
    }

    private sealed class RefuseOutboxSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxEvent>().Any(e => e.State == EntityState.Added))
            {
                throw new InvalidOperationException("Outbox save refused by test.");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("{\"title\":null}", "tank_title_invalid")]
    [InlineData("{\"title\":\" \"}", "tank_title_invalid")]
    [InlineData("{\"title\":\"Valid\",\"dimensions\":{\"width\":0,\"unit\":\"cm\"}}", "tank_dimensions_invalid")]
    [InlineData("{\"title\":\"Valid\",\"dimensions\":{\"unit\":\"inch\"}}", "tank_dimensions_invalid")]
    [InlineData("{\"title\":\"Valid\",\"volume\":{\"value\":-1,\"unit\":\"liter\"}}", "tank_volume_invalid")]
    [InlineData("{\"title\":\"Valid\",\"volume\":{\"value\":1000001,\"unit\":\"us_gallon\"}}", "tank_volume_invalid")]
    public async Task GivenInvalidTank_WhenCreated_ThenProblemCodeAndNoWrites(string body, string code)
    {
        await using var db = fixture.CreateDbContext();
        var tanksBefore = await db.Tanks.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        using var response = await SendAsync(new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(tanksBefore, await db.Tanks.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
    }

    [Fact]
    public async Task GivenLongTitle_WhenCreated_ThenTitleProblem()
    {
        using var response = await CreateAsync(new { title = new string('x', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("tank_title_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GivenAnonymousCaller_WhenCreatingTank_ThenNoTankOrEvent()
    {
        await using var db = fixture.CreateDbContext();
        var tanksBefore = await db.Tanks.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        using var response = await fixture.Client.PostAsJsonAsync("/api/tanks", new { title = "Anonymous" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(tanksBefore, await db.Tanks.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
    }

    [Fact]
    public async Task GivenNonMemberCaller_WhenCreatingTank_ThenForbiddenAndNoTankOrEvent()
    {
        var subject = Guid.NewGuid().ToString();
        await using var db = fixture.CreateDbContext();
        db.ProfileIdentities.Add(new ProfileIdentity
        {
            ProfileId = fixture.Seed.ArchivedProfile.Id,
            Provider = "oidc",
            Subject = subject,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var tanksBefore = await db.Tanks.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tanks")
        {
            Content = JsonContent.Create(new { title = "Refused member" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(subject, "archived-tank"));
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("non_member_profile", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(tanksBefore, await db.Tanks.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
    }

    [Theory]
    [InlineData("400")]
    [InlineData("403")]
    public async Task OpenApiDeclaresProblemResponses(string status)
    {
        using var response = await fixture.Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var content = json.RootElement.GetProperty("paths").GetProperty("/api/tanks").GetProperty("post")
            .GetProperty("responses").GetProperty(status).GetProperty("content");
        Assert.True(content.TryGetProperty("application/problem+json", out _));
    }

    private Task<HttpResponseMessage> CreateAsync(object body) => SendAsync(JsonContent.Create(body));

    private async Task<HttpResponseMessage> SendAsync(HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tanks") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            TestTokens.Create(Guid.NewGuid().ToString(), $"tank-{Guid.NewGuid():N}"[..24]));
        return await fixture.Client.SendAsync(request);
    }
}
