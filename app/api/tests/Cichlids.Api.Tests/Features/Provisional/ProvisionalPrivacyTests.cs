// Story: TASK-7.2
using System.Net;
using Cichlids.Domain.Enums;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using Visibility = Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// Provisional profiles and their content appear for no one but the owner: not for another member,
/// not for a visitor, and in no list, count or redirect.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-7.2")]
public sealed class ProvisionalPrivacyTests(ApiFixture fixture)
{
    private sealed record World(ProvisionalCaller Caller, long Tank, string Title, Visibility.Member Member);

    private async Task<World> NewWorldAsync()
    {
        var caller = await NewAsync(fixture.Client);
        var title = $"Hidden {Guid.NewGuid():N}";
        var tank = await CreateTankAsync(fixture.Client, caller, new { title });
        await LogAsync(fixture.Client, caller.Token, tank, new { kind = "water_change", note = "Private note" });
        using var todo = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", caller.Token, new { text = "Private todo" });
        Assert.Equal(HttpStatusCode.Created, todo.StatusCode);
        return new World(caller, tank, title, await Visibility.NewMemberAsync(fixture));
    }

    public static TheoryData<string> TankRoutes => new()
    {
        "/api/tanks/{0}",
        "/api/tanks/{0}/care",
        "/api/tanks/{0}/care/series?parameter=ph",
        "/api/tanks/{0}/care/series/all",
        "/api/tanks/{0}/care/heatmap?date=2026-10-15",
        "/api/tanks/{0}/care/plan?date=2026-10-15",
        "/api/tanks/{0}/time-lapse",
        "/api/tanks/{0}/videos",
        "/api/tanks/{0}/streaks?date=2026-10-15",
        "/api/tanks/{0}/decommissioning?date=2026-10-15",
        "/api/tanks/{0}/comments",
        "/api/tanks/{0}/follow",
    };

    [Theory]
    [MemberData(nameof(TankRoutes))]
    public async Task GivenAProvisionalTank_WhenAnotherMemberOrAVisitorReadsIt_ThenTheApiAnswersNotFound(string route)
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;
        var url = string.Format(route, world.Tank);

        using var member = await SendAsync(fixture.Client, HttpMethod.Get, url, world.Member.Token);
        using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, url, null);
        using var otherProvisional = await NewAsync(fixture.Client);
        using var provisional = await SendAsync(fixture.Client, HttpMethod.Get, url, otherProvisional.Token);

        Assert.Equal(HttpStatusCode.NotFound, member.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, provisional.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenAnotherMemberOrAVisitorAsksForTheProfileRoutes_ThenTheApiAnswersNotFound()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        foreach (var suffix in new[] { "", "/tanks", "/stories" })
        {
            var url = $"/api/profiles/{world.Caller.ProfileId}{suffix}";
            using var member = await SendAsync(fixture.Client, HttpMethod.Get, url, world.Member.Token);
            using var visitor = await SendAsync(fixture.Client, HttpMethod.Get, url, null);
            Assert.Equal(HttpStatusCode.NotFound, member.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
        }
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenTheOwnerAsksForItsTanksAndProfile_ThenTheOwnerSeesThem()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        using var tanks = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/profiles/{world.Caller.ProfileId}/tanks", world.Caller.Token);
        using var tank = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{world.Tank}", world.Caller.Token);

        Assert.Equal(HttpStatusCode.OK, tanks.StatusCode);
        Assert.Contains(world.Title, await tanks.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, tank.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenListingTanksStoriesDiscoverAndCommunity_ThenNoListShowsIt()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        foreach (var url in new[]
                 {
                     "/api/tanks", $"/api/tanks?user={world.Caller.ProfileId}", "/api/tanks?limit=100",
                     "/api/stories", $"/api/stories?user={world.Caller.ProfileId}", "/api/discover",
                     "/api/community/threads", "/api/community/categories",
                 })
        {
            foreach (var token in new string?[] { null, world.Member.Token })
            {
                using var response = await SendAsync(fixture.Client, HttpMethod.Get, url, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var text = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(world.Title, text);
                Assert.DoesNotContain($"\"profileId\":{world.Caller.ProfileId},", text);
            }
        }
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenTheAccountsOfTheLegacyRedirectsAreResolved_ThenNoRedirectLeadsToIt()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        using var byLegacyUid = await fixture.NoRedirectClient.GetAsync($"/members/{world.Caller.ProfileId}");
        using var byLegacyTank = await fixture.NoRedirectClient.GetAsync($"/tanks/details/{world.Tank}");

        Assert.NotEqual(HttpStatusCode.MovedPermanently, byLegacyUid.StatusCode);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, byLegacyTank.StatusCode);
    }

    [Fact]
    public async Task GivenAProvisionalProfile_WhenSearchingAndCountingMembers_ThenTheProfileDoesNotCount()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        using var member = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me", world.Member.Token);
        using var discover = await SendAsync(fixture.Client, HttpMethod.Get, "/api/discover", null);

        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        Assert.DoesNotContain(world.Caller.Device.CredentialId.ToString(), await discover.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GivenAProvisionalTank_WhenAMemberTriesToChangeItOrFollowIt_ThenTheApiAnswersNotFound()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;

        using var care = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{world.Tank}/care", world.Member.Token, new { kind = "water_change" });
        using var visibility = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{world.Tank}/visibility", world.Member.Token, new { profile = "public", stream = "public" });
        using var follow = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{world.Tank}/follow", world.Member.Token);
        using var comment = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{world.Tank}/comments", world.Member.Token, new { body = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, care.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, visibility.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, follow.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, comment.StatusCode);
    }

    [Fact]
    public async Task GivenTwoProvisionalCallers_WhenOneReadsTheCareOfTheOther_ThenTheApiAnswersNotFound()
    {
        var world = await NewWorldAsync();
        using var _ = world.Caller;
        using var other = await NewAsync(fixture.Client);

        using var read = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{world.Tank}/care", other.Token);
        using var write = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{world.Tank}/care", other.Token, new { kind = "water_change" });
        using var todos = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/todos", other.Token);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.DoesNotContain("Private todo", await todos.Content.ReadAsStringAsync());
    }
}
