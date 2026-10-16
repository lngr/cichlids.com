using System.Net;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// Who may change an audience: only the owner of the tank named by the route, never a requester, a follower
/// or the owner of another tank; every change runs under the lock of the tank row and changes the visibility
/// generation of the media of the tank.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TankFollowAuthorizationTests(ApiFixture fixture) : IAsyncLifetime
{
    private Member owner = null!;
    private Member otherOwner = null!;
    private Member member = null!;
    private Member third = null!;
    private readonly List<long> tanks = [];

    public async Task InitializeAsync()
    {
        owner = await NewMemberAsync(fixture);
        otherOwner = await NewMemberAsync(fixture);
        member = await NewMemberAsync(fixture);
        third = await NewMemberAsync(fixture);
    }

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.MediaLinks.Where(l => l.TankId != null && tanks.Contains(l.TankId.Value)).ExecuteDeleteAsync();
        await db.Follows.Where(f => f.TargetTankId != null && tanks.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(s => tanks.Contains(s.TankId)).ExecuteDeleteAsync();
        await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, (DateTimeOffset?)null));
        await RemoveTanksAsync(fixture, [.. tanks]);
    }

    private async Task<long> TankAsync(Member who, TankVisibility profile, TankVisibility stream)
    {
        var tank = await NewTankAsync(fixture, who.ProfileId, profile, stream);
        tanks.Add(tank);
        return tank;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null) => SendAsync(fixture, method, path, token, body);

    private async Task<HttpStatusCode> StatusAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var response = await Send(method, path, token, body);
        return response.StatusCode;
    }

    private async Task<string> StateAsync(long tank, Member who)
    {
        using var response = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", who.Token);
        return response.IsSuccessStatusCode ? (await BodyAsync(response)).GetProperty("state").GetString()! : "hidden";
    }

    private async Task<bool> SeesAsync(long tank, Member who) => await StatusAsync(HttpMethod.Get, $"/api/tanks/{tank}", who.Token) == HttpStatusCode.OK;

    private async Task<long> GenerationOfLinkedItemAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        var item = await db.MediaLinks.Where(l => l.TankId == tank).Select(l => l.MediaItemId).FirstOrDefaultAsync();
        return await db.MediaItems.Where(m => m.Id == item).Select(m => m.VisibilityGeneration).SingleAsync();
    }

    private async Task LinkItemAsync(long tank, long ownerProfile)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            OwnerProfileId = ownerProfile, Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg",
            CreatedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForTank(tank, item.Id, MediaLinkRole.Showcase));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TheOwnerOfAnotherTankCannotAnswerRequestsOrManageTheAudience()
    {
        var mine = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        var theirs = await TankAsync(otherOwner, TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{mine}/follow", member.Token)).Dispose();

        // The route names a tank of the caller, but the request exists on another tank.
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(HttpMethod.Post, $"/api/tanks/{theirs}/follow-requests/{member.ProfileId}/approve", otherOwner.Token));
        // The caller owns neither the tank of the request nor the tank in the route.
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, $"/api/tanks/{mine}/follow-requests/{member.ProfileId}/approve"),
            (HttpMethod.Post, $"/api/tanks/{mine}/follow-requests/{member.ProfileId}/decline"),
            (HttpMethod.Delete, $"/api/tanks/{mine}/followers/{member.ProfileId}"),
            (HttpMethod.Get, $"/api/tanks/{mine}/follow-requests"),
        })
        {
            using var response = await Send(method, path, otherOwner.Token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
        }

        using var add = await Send(HttpMethod.Post, $"/api/tanks/{mine}/selected-members", otherOwner.Token, new { handle = "x" });
        await AssertProblemAsync(add, HttpStatusCode.NotFound, "tank_not_found");
        Assert.Equal("requested", await StateAsync(mine, member));
        Assert.Equal("none", await StateAsync(theirs, member));
    }

    [Fact]
    public async Task ARequesterCannotApproveTheOwnRequestNorBecomeAFollowerByRepeatingIt()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", member.Token));
        for (var i = 0; i < 3; i++) (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        Assert.Equal("requested", await StateAsync(tank, member));
        Assert.False(await SeesAsync(tank, member));
        using var requests = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow-requests", owner.Token);
        Assert.Single((await BodyAsync(requests)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ACancelledOrNarrowedTankNeverTurnsAPendingRequestIntoAFollow()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "selected", stream = "selected" })).Dispose();
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "followers", stream = "followers" })).Dispose();
        Assert.Equal("requested", await StateAsync(tank, member));
        Assert.False(await SeesAsync(tank, member));
    }

    [Fact]
    public async Task AFollowFromThePublicTimeStaysAnApprovedFollowUntilTheOwnerRemovesIt()
    {
        var tank = await TankAsync(owner, TankVisibility.Public, TankVisibility.Public);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", third.Token)).Dispose();
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "followers", stream = "followers" })).Dispose();
        Assert.True(await SeesAsync(tank, member));
        Assert.Equal("following", await StateAsync(tank, third));

        (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token)).Dispose();
        Assert.False(await SeesAsync(tank, member));
        Assert.True(await SeesAsync(tank, third));
    }

    [Fact]
    public async Task ADeclinedOrRemovedMemberNeedsANewRequestThatTheOwnerApproves()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline", owner.Token)).Dispose();
        Assert.False(await SeesAsync(tank, member));
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        Assert.Equal("requested", await StateAsync(tank, member));
        Assert.False(await SeesAsync(tank, member));

        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        Assert.True(await SeesAsync(tank, member));
        (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token)).Dispose();
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        Assert.Equal("requested", await StateAsync(tank, member));
        Assert.False(await SeesAsync(tank, member));
        using var care = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");
    }

    [Fact]
    public async Task TheSelectedLevelCannotBeReachedByFollowing()
    {
        var tank = await TankAsync(owner, TankVisibility.Public, TankVisibility.Public);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        Assert.True(await SeesAsync(tank, member));
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "selected", stream = "selected" })).Dispose();
        Assert.False(await SeesAsync(tank, member));
        using var care = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/follow", third.Token));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token));
    }

    [Fact]
    public async Task AnonymousAndSuspendedCallersCannotFollowAndASuspendedFollowerLosesTheTank()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/follow", null));
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        Assert.True(await SeesAsync(tank, member));

        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == member.ProfileId).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, DateTimeOffset.UtcNow.AddDays(1)));
        }

        Assert.False(await SeesAsync(tank, member));
        using var care = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");
        var open = await TankAsync(owner, TankVisibility.Public, TankVisibility.Public);
        using var follow = await Send(HttpMethod.Put, $"/api/tanks/{open}/follow", member.Token);
        await AssertProblemAsync(follow, HttpStatusCode.Conflict, "tank_follow_not_allowed");
    }

    [Fact]
    public async Task ParallelAnswersAndRepeatedRequestsLeaveOneConsistentRelation()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        var requests = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)));
        Assert.All(requests, status => Assert.Equal(HttpStatusCode.OK, status));
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(1, await db.Follows.CountAsync(f => f.TargetTankId == tank && f.FollowerProfileId == member.ProfileId));
        }

        var answers = await Task.WhenAll(
            StatusAsync(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token),
            StatusAsync(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline", owner.Token));
        Assert.Single(answers, HttpStatusCode.NoContent);
        Assert.Single(answers, HttpStatusCode.NotFound);

        // After a removal that races with the narrowing of the tank, the member holds no access the owner took away.
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        await Task.WhenAll(
            StatusAsync(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token),
            StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "followers", stream = "selected" }),
            StatusAsync(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token));
        Assert.False(await SeesAsync(tank, member) && await StateAsync(tank, member) != "following");
        await using var check = fixture.CreateDbContext();
        var follow = await check.Follows.AsNoTracking().SingleOrDefaultAsync(f => f.TargetTankId == tank && f.FollowerProfileId == member.ProfileId);
        Assert.Equal(follow?.Status == FollowStatus.Approved, await SeesAsync(tank, member));
    }

    [Fact]
    public async Task EveryChangeOfTheAudienceChangesTheVisibilityGenerationOfTheMediaOfTheTank()
    {
        var tank = await TankAsync(owner, TankVisibility.Followers, TankVisibility.Followers);
        await LinkItemAsync(tank, owner.ProfileId);
        var handle = await HandleOfAsync(third);
        var generation = await GenerationOfLinkedItemAsync(tank);

        async Task AssertBumpAsync(Func<Task> change, bool bumps)
        {
            await change();
            var after = await GenerationOfLinkedItemAsync(tank);
            Assert.Equal(bumps, after != generation);
            generation = after;
        }

        await AssertBumpAsync(async () => (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose(), bumps: false);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline", owner.Token)).Dispose(), bumps: false);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        generation = await GenerationOfLinkedItemAsync(tank);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose(), bumps: true);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token)).Dispose(), bumps: true);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle })).Dispose(), bumps: true);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/selected-members/{third.ProfileId}", owner.Token)).Dispose(), bumps: true);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        generation = await GenerationOfLinkedItemAsync(tank);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "public", stream = "followers" })).Dispose(), bumps: true);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", third.Token)).Dispose();
        generation = await GenerationOfLinkedItemAsync(tank);
        await AssertBumpAsync(async () => (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/follow", third.Token)).Dispose(), bumps: true);
    }

    private async Task<string> HandleOfAsync(Member who)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.Where(p => p.Id == who.ProfileId).Select(p => p.Username).SingleAsync();
    }
}
