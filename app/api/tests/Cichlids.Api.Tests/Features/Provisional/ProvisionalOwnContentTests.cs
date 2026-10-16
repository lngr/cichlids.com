// Story: TASK-7.2
using System.Net;
using System.Security.Claims;
using Cichlids.Api.Features.Auth.Devices;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using Visibility = Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// What a provisional caller does with its own content: tanks that stay private, care, streaks,
/// vacations and settings, and the refusal of every wider level.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-7.2")]
public sealed class ProvisionalOwnContentTests(ApiFixture fixture)
{
    [Fact]
    public async Task GivenAProvisionalCaller_WhenCreatingATankWithoutAnAnswer_ThenTheTankIsPrivateAndTheAnswerIsStoredAsPrivate()
    {
        using var caller = await NewAsync(fixture.Client);

        var tank = await CreateTankAsync(fixture.Client, caller);

        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(caller.ProfileId, stored.ProfileId);
        Assert.Equal(TankVisibility.Private, stored.ProfileVisibility);
        Assert.Equal(TankVisibility.Private, stored.StreamVisibility);
        var consent = await db.TankVisibilityConsents.AsNoTracking().SingleAsync(c => c.TankId == tank);
        Assert.False(consent.Answer);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenCreatingATankAnsweringPrivate_ThenTheTankIsPrivate()
    {
        using var caller = await NewAsync(fixture.Client);

        var tank = await CreateTankAsync(fixture.Client, caller, new { title = "Private by answer", isPublic = false });

        using var read = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var body = await BodyAsync(read);
        Assert.Equal("private", body.GetProperty("visibility").GetProperty("profile").GetString());
        Assert.Equal("private", body.GetProperty("visibility").GetProperty("stream").GetString());
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenCreatingAPublicTank_ThenTheApiAnswersRegistrationRequiredAndStoresNothing()
    {
        using var caller = await NewAsync(fixture.Client);

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/tanks", caller.Token, new { title = "Public wish", isPublic = true });

        await AssertRegistrationRequiredAsync(response);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Tanks.AnyAsync(t => t.ProfileId == caller.ProfileId));
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("public", "private")]
    [InlineData("followers", "followers")]
    [InlineData("selected", "private")]
    public async Task GivenAProvisionalTank_WhenWideningTheVisibility_ThenTheApiAnswersRegistrationRequiredAndTheTankStaysPrivate(string profile, string stream)
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);

        using var response = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", caller.Token, new { profile, stream });

        await AssertRegistrationRequiredAsync(response);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == tank);
        Assert.Equal(TankVisibility.Private, stored.ProfileVisibility);
        Assert.Equal(TankVisibility.Private, stored.StreamVisibility);
        Assert.False(await db.TankVisibilityConsents.AnyAsync(c => c.TankId == tank && c.Answer));
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenSettingThePrivateLevelAgain_ThenTheApiAnswersOk()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);

        using var response = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/visibility", caller.Token, new { profile = "private", stream = "private" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenLoggingCareAndReadingItBack_ThenTheEntriesExistForTheOwner()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);

        var waterChange = await LogAsync(fixture.Client, caller.Token, tank, new { kind = "water_change", note = "Weekly" });
        using var measurement = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/care", caller.Token, new
        {
            kind = "measurement", values = new object[] { new { parameter = "ph", value = 7.2, origin = "test_kit" } },
        });
        Assert.Equal(HttpStatusCode.Created, measurement.StatusCode);
        using var edited = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/{waterChange}", caller.Token, new { note = "Weekly, 30 percent" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", caller.Token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(2, (await BodyAsync(list)).GetProperty("items").GetArrayLength());
        using var series = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care/series?parameter=ph", caller.Token);
        Assert.Single((await BodyAsync(series)).GetProperty("points").EnumerateArray());

        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{waterChange}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenUsingPlansRemindersAndTodos_ThenTheyWorkForTheOwner()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        var query = $"date={LocalDate}";

        using var rule = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/plan/water_change?{query}", caller.Token, new { rhythmDays = 14 });
        Assert.Equal(HttpStatusCode.OK, rule.StatusCode);
        using var reminders = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{tank}/care/plan?{query}", caller.Token, new { remindersEnabled = true });
        Assert.Equal(HttpStatusCode.OK, reminders.StatusCode);
        using var due = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/care/due?{query}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, due.StatusCode);
        using var heatmap = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/care/heatmap?{query}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, heatmap.StatusCode);

        using var todo = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", caller.Token, new { text = "Order filter wool" });
        Assert.Equal(HttpStatusCode.Created, todo.StatusCode);
        var todoId = (await BodyAsync(todo)).GetProperty("id").GetInt64();
        using var mine = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/todos", caller.Token);
        Assert.Single((await BodyAsync(mine)).GetProperty("items").EnumerateArray());
        using var done = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{todoId}", caller.Token, new { done = true });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        using var removed = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/todos/{todoId}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenRunningAGuidedWaterChange_ThenItStartsAndCanBeCancelled()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);

        using var started = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/water-changes", caller.Token);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (await BodyAsync(started)).GetProperty("id").GetInt64();
        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/water-changes/current", caller.Token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var cancelled = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/water-changes/{id}", caller.Token);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenUsingStreaksVacationsSettingsAndToday_ThenTheyWork()
    {
        using var caller = await NewAsync(fixture.Client);
        await CreateTankAsync(fixture.Client, caller);
        var query = $"date={LocalDate}";

        using var streaks = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/streaks?{query}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, streaks.StatusCode);
        using var vacation = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations?{query}", caller.Token, new { });
        Assert.Equal(HttpStatusCode.Created, vacation.StatusCode);
        using var ended = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/me/vacations/active/end?{query}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        using var vacations = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/vacations", caller.Token);
        Assert.Single((await BodyAsync(vacations)).GetProperty("items").EnumerateArray());

        using var settings = await SendAsync(fixture.Client, HttpMethod.Put, "/api/me/settings", caller.Token, new { streaksEnabled = false });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        using var read = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/settings", caller.Token);
        Assert.False((await BodyAsync(read)).GetProperty("streaksEnabled").GetBoolean());

        using var today = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/today?{query}", caller.Token);
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
        var body = await BodyAsync(today);
        Assert.Equal(1, body.GetProperty("tanks").GetArrayLength());
        Assert.Equal(0, body.GetProperty("draftCount").GetInt32());
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenReadingTheProfileOfTheCaller_ThenItIsMarkedProvisional()
    {
        using var caller = await NewAsync(fixture.Client);

        using var me = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", caller.Token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True((await BodyAsync(me)).GetProperty("provisional").GetBoolean());
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenReadingATankOfAMember_ThenTheCallerSeesOnlyWhatAVisitorSees()
    {
        using var caller = await NewAsync(fixture.Client);
        var owner = await Visibility.NewMemberAsync(fixture);
        var open = await Visibility.NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var followersOnly = await Visibility.NewTankAsync(fixture, owner.ProfileId, TankVisibility.Followers, TankVisibility.Followers);
        var privateTank = await Visibility.NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private);

        foreach (var (tank, expected) in new[] { (open, HttpStatusCode.OK), (followersOnly, HttpStatusCode.NotFound), (privateTank, HttpStatusCode.NotFound) })
        {
            using var provisional = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}", caller.Token);
            using var anonymous = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}", null);
            Assert.Equal(expected, provisional.StatusCode);
            Assert.Equal(expected, anonymous.StatusCode);
            using var care = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", caller.Token);
            Assert.Equal(expected == HttpStatusCode.OK ? HttpStatusCode.OK : HttpStatusCode.NotFound, care.StatusCode);
        }
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenWritingToATankOfAMember_ThenTheApiAnswersNotFound()
    {
        using var caller = await NewAsync(fixture.Client);
        var owner = await Visibility.NewMemberAsync(fixture);
        var open = await Visibility.NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);

        using var care = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{open}/care", caller.Token, new { kind = "water_change" });
        using var visibility = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{open}/visibility", caller.Token, new { profile = "private", stream = "private" });
        using var todo = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{open}/todos", caller.Token, new { text = "Not mine" });

        Assert.Equal(HttpStatusCode.NotFound, care.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, visibility.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, todo.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(TankVisibility.Public, (await db.Tanks.AsNoTracking().SingleAsync(t => t.Id == open)).ProfileVisibility);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenTheTankAccessIsResolved_ThenTheCallerIsTheOwnerOfItsOwnTanksAndAnonymousElsewhere()
    {
        using var caller = await NewAsync(fixture.Client);
        var own = await CreateTankAsync(fixture.Client, caller);
        var member = await Visibility.NewMemberAsync(fixture);
        var open = await Visibility.NewTankAsync(fixture, member.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var closed = await Visibility.NewTankAsync(fixture, member.ProfileId, TankVisibility.Followers, TankVisibility.Private);
        using var scope = fixture.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TankAccessService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", caller.ProfileId.ToString()), new Claim("provisional", "true"), new Claim("cid", caller.Device.CredentialId.ToString())],
            DeviceTokenAuthentication.SchemeName));

        var ownAccess = await access.FindAsync(own, principal, CancellationToken.None);
        var openAccess = await access.FindAsync(open, principal, CancellationToken.None);
        var closedAccess = await access.FindAsync(closed, principal, CancellationToken.None);

        Assert.True(ownAccess!.IsOwner);
        Assert.True(ownAccess.CanSeeStream);
        Assert.Equal(TankViewer.Member, openAccess!.Viewer);
        Assert.True(openAccess.CanSeeProfile);
        Assert.False(closedAccess!.CanSeeProfile);
        Assert.False(closedAccess.CanSeeStream);
    }

    [Fact]
    public async Task GivenAFileOfAProvisionalTank_WhenTheMediaVisibilityIsAsked_ThenOnlyTheOwnerSeesIt()
    {
        using var caller = await NewAsync(fixture.Client);
        var tank = await CreateTankAsync(fixture.Client, caller);
        var member = await Visibility.NewMemberAsync(fixture);
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            OwnerProfileId = caller.ProfileId, Kind = MediaKind.Photo, StorageKey = $"private/test/{Guid.NewGuid():N}.jpg",
            CreatedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForTank(tank, item.Id, MediaLinkRole.TimeLapse));
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;

        var owner = await db.VisibleMediaItemIdsAsync([item.Id], caller.ProfileId, now);
        var other = await db.VisibleMediaItemIdsAsync([item.Id], member.ProfileId, now);
        var anonymous = await db.VisibleMediaItemIdsAsync([item.Id], null, now);

        Assert.Contains(item.Id, owner);
        Assert.Empty(other);
        Assert.Empty(anonymous);
        Assert.True((await db.VisibleMediaGenerationsAsync([item.Id], caller.ProfileId, now)).ContainsKey(item.Id));
    }
}
