using System.Net;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// Following a tank: at once for a public tank, by a request the owner answers for a tank whose profile is
/// for followers, and the matching owner routes to approve, decline and remove, with an outbox event per
/// change.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TankFollowEndpointsTests(ApiFixture fixture) : IAsyncLifetime
{
    private Member owner = null!;
    private Member member = null!;
    private Member other = null!;
    private readonly List<long> tanks = [];

    public async Task InitializeAsync()
    {
        owner = await NewMemberAsync(fixture);
        member = await NewMemberAsync(fixture);
        other = await NewMemberAsync(fixture);
    }

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Follows.Where(f => f.TargetTankId != null && tanks.Contains(f.TargetTankId.Value)).ExecuteDeleteAsync();
        await db.TankSelectedMembers.Where(s => tanks.Contains(s.TankId)).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, [.. tanks]);
    }

    private async Task<long> TankAsync(TankVisibility profile, TankVisibility stream)
    {
        var tank = await NewTankAsync(fixture, owner.ProfileId, profile, stream);
        tanks.Add(tank);
        return tank;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null) => SendAsync(fixture, method, path, token, body);

    private async Task<List<string>> EventsAsync(long tank)
    {
        await using var db = fixture.CreateDbContext();
        return await db.OutboxEvents.Where(e => e.AggregateType == "tank" && e.AggregateId == tank.ToString() && e.EventType.StartsWith("tank.follow")
                || e.AggregateId == tank.ToString() && e.EventType == "tank.unfollowed").OrderBy(e => e.Id).Select(e => e.EventType).ToListAsync();
    }

    [Fact]
    public async Task GivenAPublicTank_WhenAMemberFollowsIt_ThenTheMemberFollowsAtOnceAndCanStopFollowing()
    {
        var tank = await TankAsync(TankVisibility.Public, TankVisibility.Public);
        using var before = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        var beforeBody = await BodyAsync(before);
        Assert.Equal("none", beforeBody.GetProperty("state").GetString());
        Assert.True(beforeBody.GetProperty("canFollow").GetBoolean());

        using var follow = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        Assert.Equal("following", (await BodyAsync(follow)).GetProperty("state").GetString());
        using var again = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("following", (await BodyAsync(again)).GetProperty("state").GetString());
        Assert.Equal(["tank.followed"], await EventsAsync(tank));

        using var detail = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        Assert.Equal("following", (await BodyAsync(detail)).GetProperty("follow").GetProperty("state").GetString());

        using var stop = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
        using var after = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("none", (await BodyAsync(after)).GetProperty("state").GetString());
        Assert.Equal(["tank.followed", "tank.unfollowed"], await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenATankForFollowers_WhenAMemberAsksAndTheOwnerApproves_ThenOnlyThatMemberSeesTheTankAndItsStream()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        using var hidden = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "tank_not_found");

        using var teaser = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        var teaserBody = await BodyAsync(teaser);
        Assert.Equal("none", teaserBody.GetProperty("state").GetString());
        Assert.True(teaserBody.GetProperty("canFollow").GetBoolean());
        Assert.Equal(owner.ProfileId, teaserBody.GetProperty("teaser").GetProperty("author").GetProperty("id").GetInt64());

        using var request = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("requested", (await BodyAsync(request)).GetProperty("state").GetString());
        using var stillHidden = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        await AssertProblemAsync(stillHidden, HttpStatusCode.NotFound, "tank_not_found");
        using var care = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");

        using var requests = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow-requests", owner.Token);
        var listed = (await BodyAsync(requests)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(member.ProfileId, listed.GetProperty("member").GetProperty("id").GetInt64());
        using var detailOfOwner = await Send(HttpMethod.Get, $"/api/tanks/{tank}", owner.Token);
        Assert.Equal(1, (await BodyAsync(detailOfOwner)).GetProperty("audience").GetProperty("pendingRequests").GetInt32());

        using var approve = await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        using var visible = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        var visibleBody = await BodyAsync(visible);
        Assert.Equal("following", visibleBody.GetProperty("follow").GetProperty("state").GetString());
        Assert.True(!visibleBody.GetProperty("follow").TryGetProperty("teaser", out var teaserOfFollower)
            || teaserOfFollower.ValueKind == System.Text.Json.JsonValueKind.Null);
        using var streamVisible = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        Assert.Equal(HttpStatusCode.OK, streamVisible.StatusCode);

        using var others = await Send(HttpMethod.Get, $"/api/tanks/{tank}", other.Token);
        await AssertProblemAsync(others, HttpStatusCode.NotFound, "tank_not_found");
        using var anonymous = await Send(HttpMethod.Get, $"/api/tanks/{tank}", null);
        await AssertProblemAsync(anonymous, HttpStatusCode.NotFound, "tank_not_found");
        Assert.Equal(["tank.follow_requested", "tank.follow_approved"], await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenAnOpenRequest_WhenTheOwnerDeclines_ThenTheRequestIsGoneAndTheMemberCanAskAgain()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        using var decline = await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline", owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);
        using var state = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("none", (await BodyAsync(state)).GetProperty("state").GetString());
        using var again = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("requested", (await BodyAsync(again)).GetProperty("state").GetString());
        Assert.Equal(["tank.follow_requested", "tank.follow_declined", "tank.follow_requested"], await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenAnOpenRequest_WhenTheMemberWithdrawsIt_ThenTheOwnerHasNothingToAnswer()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        using var withdraw = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        using var requests = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow-requests", owner.Token);
        Assert.Empty((await BodyAsync(requests)).GetProperty("items").EnumerateArray());
        using var approve = await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token);
        await AssertProblemAsync(approve, HttpStatusCode.NotFound, "follow_request_not_found");
        Assert.Equal(["tank.follow_requested", "tank.follow_withdrawn"], await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenAnApprovedFollower_WhenTheOwnerRemovesTheFollower_ThenTheMemberLosesTheTankAtOnce()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        using var followers = await Send(HttpMethod.Get, $"/api/tanks/{tank}/followers", owner.Token);
        Assert.Equal(member.ProfileId, (await BodyAsync(followers)).GetProperty("items").EnumerateArray().Single().GetProperty("member").GetProperty("id").GetInt64());

        using var remove = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var detail = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        await AssertProblemAsync(detail, HttpStatusCode.NotFound, "tank_not_found");
        using var care = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(care, HttpStatusCode.NotFound, "care_not_found");
        using var second = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token);
        await AssertProblemAsync(second, HttpStatusCode.NotFound, "follower_not_found");
        Assert.Equal(["tank.follow_requested", "tank.follow_approved", "tank.follower_removed"], await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenOpenRequests_WhenTheOwnerMakesTheTankPublic_ThenTheRequestsAreApproved()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Private);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        using var visibility = await Send(HttpMethod.Put, $"/api/tanks/{tank}/visibility", owner.Token, new { profile = "public", stream = "private" });
        Assert.Equal(HttpStatusCode.OK, visibility.StatusCode);
        using var state = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("following", (await BodyAsync(state)).GetProperty("state").GetString());
        Assert.Contains("tank.follow_approved", await EventsAsync(tank));
    }

    [Fact]
    public async Task GivenATankThatIsNotOpenForFollowers_WhenAMemberFollows_ThenTheAnswerDoesNotRevealTheTank()
    {
        var selected = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        var priv = await TankAsync(TankVisibility.Private, TankVisibility.Private);
        foreach (var tank in new[] { selected, priv })
        {
            using var follow = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token);
            await AssertProblemAsync(follow, HttpStatusCode.NotFound, "tank_not_found");
            using var state = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
            await AssertProblemAsync(state, HttpStatusCode.NotFound, "tank_not_found");
        }

        using var anonymous = await Send(HttpMethod.Put, $"/api/tanks/{selected}/follow", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(await EventsAsync(selected));
    }

    [Fact]
    public async Task GivenATankForFollowers_ThenAnonymousCallersCannotAskNorSeeTheTeaser()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        using var state = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", null);
        await AssertProblemAsync(state, HttpStatusCode.NotFound, "tank_not_found");
        using var follow = await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", null);
        Assert.Equal(HttpStatusCode.Unauthorized, follow.StatusCode);
    }

    [Fact]
    public async Task TheOwnerCannotFollowTheOwnTank_AndAMemberWhoIsSelectedAtTheSelectedLevelIsToldSo()
    {
        var followers = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        using var own = await Send(HttpMethod.Put, $"/api/tanks/{followers}/follow", owner.Token);
        await AssertProblemAsync(own, HttpStatusCode.Conflict, "tank_follow_not_allowed");

        var selected = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        await using (var db = fixture.CreateDbContext())
        {
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = selected, MemberProfileId = member.ProfileId, AddedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        using var selectedFollow = await Send(HttpMethod.Put, $"/api/tanks/{selected}/follow", member.Token);
        await AssertProblemAsync(selectedFollow, HttpStatusCode.Conflict, "tank_follow_not_allowed");
        using var state = await Send(HttpMethod.Get, $"/api/tanks/{selected}/follow", member.Token);
        var body = await BodyAsync(state);
        Assert.False(body.GetProperty("canFollow").GetBoolean());
    }

    [Fact]
    public async Task TheAudienceRoutes_AnswerOnlyTheOwnerOfTheTank()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, $"/api/tanks/{tank}/followers"),
            (HttpMethod.Get, $"/api/tanks/{tank}/follow-requests"),
            (HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve"),
            (HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline"),
            (HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}"),
            (HttpMethod.Get, $"/api/tanks/{tank}/selected-members"),
            (HttpMethod.Delete, $"/api/tanks/{tank}/selected-members/{member.ProfileId}"),
        })
        {
            foreach (var token in new[] { member.Token, other.Token })
            {
                using var response = await Send(method, path, token);
                await AssertProblemAsync(response, HttpStatusCode.NotFound, "tank_not_found");
            }

            using var anonymous = await Send(method, path, null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var add = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", other.Token, new { handle = "anyone" });
        await AssertProblemAsync(add, HttpStatusCode.NotFound, "tank_not_found");
        using var state = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("requested", (await BodyAsync(state)).GetProperty("state").GetString());
    }

    private async Task<string> HandleOfAsync(Member who)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Profiles.Where(p => p.Id == who.ProfileId).Select(p => p.Username).SingleAsync();
    }

    [Fact]
    public async Task GivenATankForSelectedMembers_WhenTheOwnerAddsAMemberByHandle_ThenOnlyThatMemberSeesIt_AndRemovingEndsTheAccess()
    {
        var tank = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        var handle = await HandleOfAsync(member);
        using var add = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = $"@{handle.ToUpperInvariant()}" });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(member.ProfileId, (await BodyAsync(add)).GetProperty("member").GetProperty("id").GetInt64());
        using var again = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using var list = await Send(HttpMethod.Get, $"/api/tanks/{tank}/selected-members", owner.Token);
        Assert.Single((await BodyAsync(list)).EnumerateArray());
        using var seen = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        using var notSeen = await Send(HttpMethod.Get, $"/api/tanks/{tank}", other.Token);
        await AssertProblemAsync(notSeen, HttpStatusCode.NotFound, "tank_not_found");

        using var remove = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/selected-members/{member.ProfileId}", owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var gone = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "tank_not_found");
        using var removeAgain = await Send(HttpMethod.Delete, $"/api/tanks/{tank}/selected-members/{member.ProfileId}", owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, removeAgain.StatusCode);

        await using var db = fixture.CreateDbContext();
        var events = await db.OutboxEvents.Where(e => e.AggregateId == tank.ToString() && e.EventType.StartsWith("tank.selected_member"))
            .OrderBy(e => e.Id).Select(e => e.EventType).ToListAsync();
        Assert.Equal(["tank.selected_member_added", "tank.selected_member_removed"], events);
    }

    [Fact]
    public async Task AddingASelectedMember_RefusesUnknownHandlesTheOwnerAndEmptyInput()
    {
        var tank = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        using var unknown = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = "nobody-by-this-name" });
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "selected_member_profile_not_found");
        using var self = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = await HandleOfAsync(owner) });
        await AssertProblemAsync(self, HttpStatusCode.BadRequest, "selected_member_is_owner");
        foreach (var body in new object[] { new { handle = "  " }, new { handle = (string?)null }, new { } })
        {
            using var invalid = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, body);
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "selected_member_handle_invalid");
        }
    }

    [Fact]
    public async Task AFollowerAndASelectedMemberAreBothReachedByTheFollowersLevelButOnlyTheSelectedMemberByTheSelectedStream()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Selected);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = await HandleOfAsync(other) })).Dispose();

        using var followerProfile = await Send(HttpMethod.Get, $"/api/tanks/{tank}", member.Token);
        Assert.Equal(HttpStatusCode.OK, followerProfile.StatusCode);
        using var followerStream = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", member.Token);
        await AssertProblemAsync(followerStream, HttpStatusCode.NotFound, "care_not_found");
        using var selectedProfile = await Send(HttpMethod.Get, $"/api/tanks/{tank}", other.Token);
        Assert.Equal(HttpStatusCode.OK, selectedProfile.StatusCode);
        using var selectedStream = await Send(HttpMethod.Get, $"/api/tanks/{tank}/care", other.Token);
        Assert.Equal(HttpStatusCode.OK, selectedStream.StatusCode);
    }

    // The parts of a problem answer that are not specific to the request.
    private static async Task<string> ShapeAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return string.Join("|", new[] { "status", "title", "detail", "code" }.Select(name => body.TryGetProperty(name, out var value) ? value.ToString() : ""));
    }

    [Fact]
    public async Task EveryFollowRouteAnswersATankTheCallerMayNotSeeExactlyLikeAMissingTank()
    {
        var priv = await TankAsync(TankVisibility.Private, TankVisibility.Private);
        var selected = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        var draft = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        var deleted = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Tanks.Where(t => t.Id == draft).ExecuteUpdateAsync(x => x.SetProperty(t => t.State, TankState.Draft));
            await db.Tanks.Where(t => t.Id == deleted).ExecuteUpdateAsync(x => x.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
        }

        const long missing = 9_000_000_000;
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using var reference = await Send(method, $"/api/tanks/{missing}/follow", member.Token);
            var expectedBody = await ShapeAsync(reference);
            foreach (var tank in new[] { priv, selected, draft, deleted })
            {
                using var response = await Send(method, $"/api/tanks/{tank}/follow", member.Token);
                Assert.Equal(reference.StatusCode, response.StatusCode);
                Assert.Equal(reference.Content.Headers.ContentType, response.Content.Headers.ContentType);
                Assert.Equal(expectedBody, await ShapeAsync(response));
            }
        }
    }

    [Fact]
    public async Task TheTeaserOfATankForFollowersNamesOnlyTheOwner()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        using var response = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        var teaser = (await BodyAsync(response)).GetProperty("teaser");
        Assert.Equal(["author"], teaser.EnumerateObject().Select(p => p.Name).ToList());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("audience", body);
        Assert.DoesNotContain("title", body);
    }

    [Fact]
    public async Task ARequesterSeesOnlyTheOwnStateAndAThirdPartySeesNothingOfRequestsDeclinesOrRemovals()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        using var third = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", other.Token);
        Assert.Equal("none", (await BodyAsync(third)).GetProperty("state").GetString());
        var before = await third.Content.ReadAsStringAsync();

        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/decline", owner.Token)).Dispose();
        using var afterDecline = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", other.Token);
        Assert.Equal(before, await afterDecline.Content.ReadAsStringAsync());
        using var requesterAfterDecline = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal(before.Replace("\"none\"", "\"none\""), await requesterAfterDecline.Content.ReadAsStringAsync());

        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        (await Send(HttpMethod.Delete, $"/api/tanks/{tank}/followers/{member.ProfileId}", owner.Token)).Dispose();
        using var afterRemoval = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", other.Token);
        Assert.Equal(before, await afterRemoval.Content.ReadAsStringAsync());
        using var removed = await Send(HttpMethod.Get, $"/api/tanks/{tank}/follow", member.Token);
        Assert.Equal("none", (await BodyAsync(removed)).GetProperty("state").GetString());
        foreach (var path in new[] { "followers", "follow-requests", "selected-members" })
        {
            using var list = await Send(HttpMethod.Get, $"/api/tanks/{tank}/{path}", other.Token);
            await AssertProblemAsync(list, HttpStatusCode.NotFound, "tank_not_found");
        }
    }

    [Fact]
    public async Task TheOutboxPayloadsOfFollowAndSelectionEventsCarryOnlyIds()
    {
        var tank = await TankAsync(TankVisibility.Followers, TankVisibility.Followers);
        (await Send(HttpMethod.Put, $"/api/tanks/{tank}/follow", member.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/follow-requests/{member.ProfileId}/approve", owner.Token)).Dispose();
        (await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = await HandleOfAsync(other) })).Dispose();
        await using var db = fixture.CreateDbContext();
        var payloads = await db.OutboxEvents.Where(e => e.AggregateId == tank.ToString()
            && (e.EventType.StartsWith("tank.follow") || e.EventType.StartsWith("tank.selected_member"))).Select(e => e.Payload).ToListAsync();
        Assert.Equal(3, payloads.Count);
        foreach (var payload in payloads)
        {
            var names = System.Text.Json.JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
            Assert.Equal(["memberProfileId", "ownerProfileId", "tankId"], names);
        }
    }

    [Fact]
    public async Task AddingASelectedMember_AnswersASuspendedOrNonMemberProfileLikeAnUnknownHandle()
    {
        var tank = await TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        var handle = await HandleOfAsync(other);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Profiles.Where(p => p.Id == other.ProfileId).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, DateTimeOffset.UtcNow.AddDays(1)));
        }

        using var unknown = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle = "nobody-by-this-name" });
        using var suspended = await Send(HttpMethod.Post, $"/api/tanks/{tank}/selected-members", owner.Token, new { handle });
        Assert.Equal(unknown.StatusCode, suspended.StatusCode);
        Assert.Equal(await ShapeAsync(unknown), await ShapeAsync(suspended));
        await using var check = fixture.CreateDbContext();
        Assert.Empty(await check.TankSelectedMembers.Where(s => s.TankId == tank).ToListAsync());
    }
}

