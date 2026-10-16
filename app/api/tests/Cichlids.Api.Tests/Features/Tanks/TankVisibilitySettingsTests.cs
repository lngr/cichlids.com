using System.Net;
using System.Text.Json;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public sealed class TankVisibilitySettingsTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.TankVisibilityConsents.Where(c => tanks.Contains(c.TankId)).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, tanks.ToArray());
    }

    private async Task<(Member Owner, long Tank)> TankAsync(TankVisibility profile, TankVisibility stream)
    {
        var owner = await NewMemberAsync(fixture);
        var tank = await NewTankAsync(fixture, owner.ProfileId, profile, stream);
        tanks.Add(tank);
        return (owner, tank);
    }

    private Task<HttpResponseMessage> PutAsync(long tank, string? token, object body) =>
        SendAsync(fixture, HttpMethod.Put, $"/api/tanks/{tank}/visibility", token, body);

    [Fact]
    public async Task GivenAPublicTank_WhenTheOwnerMakesItPrivate_ThenBothLevelsChangeAnEventIsRecordedAndTheMediaAreQueuedForPlacement()
    {
        var (owner, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Public);

        using var response = await PutAsync(tank, owner.Token, new { profile = "private", stream = "private" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("private", body.GetProperty("profile").GetString());
        Assert.Equal("private", body.GetProperty("stream").GetString());
        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(TankVisibility.Private, stored.ProfileVisibility);
        Assert.Equal(TankVisibility.Private, stored.StreamVisibility);
        Assert.True(stored.MediaMovePending);
        Assert.True(stored.MediaPlacedPublic);
        var evt = Assert.Single(await db.OutboxEvents.AsNoTracking()
            .Where(e => e.EventType == "tank.visibility_changed" && e.AggregateId == tank.ToString()).ToListAsync());
        Assert.Equal("tank", evt.AggregateType);
        using var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal(tank, payload.RootElement.GetProperty("tankId").GetInt64());
        Assert.Equal(owner.ProfileId, payload.RootElement.GetProperty("profileId").GetInt64());
        Assert.Equal("private", payload.RootElement.GetProperty("profileVisibility").GetString());
        Assert.Equal("private", payload.RootElement.GetProperty("streamVisibility").GetString());
        Assert.Equal("public", payload.RootElement.GetProperty("previousProfileVisibility").GetString());
        Assert.Equal("public", payload.RootElement.GetProperty("previousStreamVisibility").GetString());
        var consent = Assert.Single(await db.TankVisibilityConsents.AsNoTracking().Where(c => c.TankId == tank).ToListAsync());
        Assert.False(consent.Answer);
        Assert.Equal(TankVisibilityConsent.SettingsContext, consent.Context);
    }

    [Fact]
    public async Task GivenAPrivateTank_WhenTheOwnerMakesItPublic_ThenTheConsentIsStoredAndTheMediaAreQueued()
    {
        var (owner, tank) = await TankAsync(TankVisibility.Private, TankVisibility.Private);

        using var response = await PutAsync(tank, owner.Token, new { profile = "public", stream = "public" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(TankVisibility.Public, stored.ProfileVisibility);
        Assert.Equal(TankVisibility.Public, stored.StreamVisibility);
        Assert.True(stored.MediaMovePending);
        Assert.False(stored.MediaPlacedPublic);
        var consent = Assert.Single(await db.TankVisibilityConsents.AsNoTracking().Where(c => c.TankId == tank).ToListAsync());
        Assert.True(consent.Answer);
    }

    [Fact]
    public async Task GivenAPublicTank_WhenOnlyTheStreamIsRestricted_ThenTheMediaStayAndNoConsentIsStored()
    {
        var (owner, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Public);

        using var response = await PutAsync(tank, owner.Token, new { profile = "public", stream = "private" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(TankVisibility.Public, stored.ProfileVisibility);
        Assert.Equal(TankVisibility.Private, stored.StreamVisibility);
        Assert.False(stored.MediaMovePending);
        Assert.Empty(await db.TankVisibilityConsents.Where(c => c.TankId == tank).ToListAsync());
        Assert.Single(await db.OutboxEvents.Where(e => e.EventType == "tank.visibility_changed" && e.AggregateId == tank.ToString()).ToListAsync());
    }

    [Theory]
    [InlineData(TankVisibility.Private, TankVisibility.Private, "private", "public")]
    [InlineData(TankVisibility.Public, TankVisibility.Private, "private", "public")]
    [InlineData(TankVisibility.Public, TankVisibility.Public, "private", "public")]
    public async Task GivenAStreamWiderThanTheProfile_WhenChangingTheLevels_ThenARefusalAndNoChange(
        TankVisibility profile, TankVisibility stream, string requestedProfile, string requestedStream)
    {
        var (owner, tank) = await TankAsync(profile, stream);

        using var response = await PutAsync(tank, owner.Token, new { profile = requestedProfile, stream = requestedStream });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "tank_stream_wider_than_profile");
        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(profile, stored.ProfileVisibility);
        Assert.Equal(stream, stored.StreamVisibility);
        Assert.Empty(await db.OutboxEvents.Where(e => e.EventType == "tank.visibility_changed" && e.AggregateId == tank.ToString()).ToListAsync());
    }

    [Theory]
    [InlineData("{\"profile\":\"followers\",\"stream\":\"private\"}")]
    [InlineData("{\"profile\":\"public\",\"stream\":\"selected_members\"}")]
    [InlineData("{\"profile\":\"public\"}")]
    [InlineData("{\"stream\":\"public\"}")]
    [InlineData("{}")]
    public async Task GivenAnUnknownOrMissingLevel_WhenChangingTheLevels_ThenACodedProblem(string body)
    {
        var (owner, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Public);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tanks/{tank}/visibility")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);
        using var response = await fixture.Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "tank_visibility_invalid");
    }

    [Fact]
    public async Task GivenTheCurrentLevels_WhenTheOwnerSendsThemAgain_ThenNothingIsRecorded()
    {
        var (owner, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Private);

        using var response = await PutAsync(tank, owner.Token, new { profile = "public", stream = "private" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.OutboxEvents.Where(e => e.EventType == "tank.visibility_changed" && e.AggregateId == tank.ToString()).ToListAsync());
        Assert.Empty(await db.TankVisibilityConsents.Where(c => c.TankId == tank).ToListAsync());
    }

    [Fact]
    public async Task GivenAnotherMember_WhenChangingTheLevels_ThenTheTankIsNotFoundAndNothingChanges()
    {
        var (_, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Public);
        var other = await NewMemberAsync(fixture);

        using var response = await PutAsync(tank, other.Token, new { profile = "private", stream = "private" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(TankVisibility.Public, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank)).ProfileVisibility);
    }

    [Fact]
    public async Task GivenNoLogin_WhenChangingTheLevels_ThenUnauthorized()
    {
        var (_, tank) = await TankAsync(TankVisibility.Public, TankVisibility.Public);

        using var response = await PutAsync(tank, null, new { profile = "private", stream = "private" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GivenAMissingTank_WhenChangingTheLevels_ThenTheTankIsNotFound()
    {
        var owner = await NewMemberAsync(fixture);

        using var response = await PutAsync(long.MaxValue, owner.Token, new { profile = "private", stream = "private" });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
    }
}
