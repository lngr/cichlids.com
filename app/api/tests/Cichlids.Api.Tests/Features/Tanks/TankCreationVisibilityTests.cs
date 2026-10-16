using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public sealed class TankCreationVisibilityTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("{\"title\":\"No answer\"}")]
    [InlineData("{\"title\":\"Null answer\",\"isPublic\":null}")]
    public async Task GivenNoAnswer_WhenCreatingATank_ThenACodedProblemAndNoWrites(string body)
    {
        var owner = await NewMemberAsync(fixture);
        await using var db = fixture.CreateDbContext();
        var tanksBefore = await db.Tanks.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        var consentsBefore = await db.TankVisibilityConsents.CountAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tanks")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);
        using var response = await fixture.Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "tank_visibility_answer_required");
        Assert.Equal(tanksBefore, await db.Tanks.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
        Assert.Equal(consentsBefore, await db.TankVisibilityConsents.CountAsync());
    }

    [Fact]
    public async Task GivenYes_WhenCreatingATank_ThenProfileAndStreamArePublicAndTheConsentIsStoredWithItsTime()
    {
        var owner = await NewMemberAsync(fixture);
        var before = DateTimeOffset.UtcNow;

        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/tanks", owner.Token, new { title = "Public yes", isPublic = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal("public", body.GetProperty("visibility").GetProperty("profile").GetString());
        Assert.Equal("public", body.GetProperty("visibility").GetProperty("stream").GetString());
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(TankVisibility.Public, tank.ProfileVisibility);
        Assert.Equal(TankVisibility.Public, tank.StreamVisibility);
        Assert.True(tank.MediaPlacedPublic);
        var consent = await db.TankVisibilityConsents.AsNoTracking().SingleAsync(c => c.TankId == id);
        Assert.True(consent.Answer);
        Assert.Equal(TankVisibilityConsent.CreationContext, consent.Context);
        Assert.InRange(consent.AnsweredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var created = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.EventType == "tank.created" && e.AggregateId == id.ToString());
        using var payload = JsonDocument.Parse(created.Payload);
        Assert.Equal("public", payload.RootElement.GetProperty("profileVisibility").GetString());
        Assert.Equal("public", payload.RootElement.GetProperty("streamVisibility").GetString());
        await RemoveTanksAsync(fixture, id);
    }

    [Fact]
    public async Task GivenNo_WhenCreatingATank_ThenProfileAndStreamArePrivateAndTheAnswerIsStored()
    {
        var owner = await NewMemberAsync(fixture);

        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/tanks", owner.Token, new { title = "Private no", isPublic = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal("private", body.GetProperty("visibility").GetProperty("profile").GetString());
        Assert.Equal("private", body.GetProperty("visibility").GetProperty("stream").GetString());
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(TankVisibility.Private, tank.ProfileVisibility);
        Assert.Equal(TankVisibility.Private, tank.StreamVisibility);
        Assert.False(tank.MediaPlacedPublic);
        Assert.False((await db.TankVisibilityConsents.AsNoTracking().SingleAsync(c => c.TankId == id)).Answer);
        await RemoveTanksAsync(fixture, id);
    }

    [Fact]
    public async Task GivenNoAnswer_WhenOnboardingWithANewTank_ThenACodedProblemAndNoTank()
    {
        var owner = await NewMemberAsync(fixture);
        var title = $"Onboarding no answer {Guid.NewGuid():N}";

        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/onboarding", owner.Token,
            new { tank = new { title, region = "lake_malawi" } });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "tank_visibility_answer_required");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Tanks.AnyAsync(t => t.Title == title));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAnAnswer_WhenOnboardingWithANewTank_ThenTheTankHasThatLevelAndConsent(bool isPublic)
    {
        var owner = await NewMemberAsync(fixture);
        var title = $"Onboarding answer {Guid.NewGuid():N}";

        using var response = await SendAsync(fixture, HttpMethod.Post, "/api/me/onboarding", owner.Token,
            new { tank = new { title, region = "lake_malawi", isPublic } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = (await BodyAsync(response)).GetProperty("tankId").GetInt64();
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(isPublic ? TankVisibility.Public : TankVisibility.Private, tank.ProfileVisibility);
        Assert.Equal(isPublic ? TankVisibility.Public : TankVisibility.Private, tank.StreamVisibility);
        Assert.Equal(isPublic, (await db.TankVisibilityConsents.AsNoTracking().SingleAsync(c => c.TankId == id)).Answer);
        await db.TankVisibilityConsents.Where(c => c.TankId == id).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, id);
    }
}
