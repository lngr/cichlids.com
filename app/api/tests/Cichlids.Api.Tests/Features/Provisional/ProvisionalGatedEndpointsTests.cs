// Story: TASK-7.2
using System.Net;
using System.Net.Http.Json;
using Cichlids.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Provisional.ProvisionalTestSupport;
using Visibility = Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// The routes a provisional caller (device token) must not reach: everything that others see or that
/// belongs to registered members answers 403 with the code registration_required, and nothing is
/// created on the way.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "TASK-7.2")]
public sealed class ProvisionalGatedEndpointsTests(ApiFixture fixture)
{
    /// <summary>
    /// The routes that need a registered member, as method and path of a request.
    /// </summary>
    public static TheoryData<string, string> GatedRoutes => new()
    {
        { "PUT", "/api/tanks/1/follow" },
        { "DELETE", "/api/tanks/1/follow" },
        { "GET", "/api/tanks/1/followers" },
        { "DELETE", "/api/tanks/1/followers/2" },
        { "GET", "/api/tanks/1/follow-requests" },
        { "POST", "/api/tanks/1/follow-requests/2/approve" },
        { "POST", "/api/tanks/1/follow-requests/2/decline" },
        { "GET", "/api/tanks/1/selected-members" },
        { "POST", "/api/tanks/1/selected-members" },
        { "DELETE", "/api/tanks/1/selected-members/2" },
        { "POST", "/api/tanks/1/comments" },
        { "POST", "/api/stories/1/comments" },
        { "GET", "/api/me/stories" },
        { "POST", "/api/me/stories" },
        { "GET", "/api/me/stories/1" },
        { "PUT", "/api/me/stories/1" },
        { "DELETE", "/api/me/stories/1" },
        { "POST", "/api/me/stories/1/withdraw" },
        { "POST", "/api/me/stories/1/items" },
        { "PUT", "/api/me/stories/1/items/2" },
        { "DELETE", "/api/me/stories/1/items/2" },
        { "PUT", "/api/me/stories/1/order" },
        { "PUT", "/api/me/stories/1/cover" },
        { "POST", "/api/me/stories/1/subjects" },
        { "PUT", "/api/me/stories/1/subjects/2" },
        { "DELETE", "/api/me/stories/1/subjects/2" },
        { "POST", "/api/me/media/1/share" },
        { "POST", "/api/uploads" },
        { "GET", "/api/me/drafts" },
        { "POST", "/api/stories/1/publish" },
        { "DELETE", "/api/stories/1" },
        { "PUT", "/api/me/avatar" },
        { "POST", "/api/me/avatar/photo" },
        { "DELETE", "/api/me/avatar" },
        { "PUT", "/api/me/header" },
        { "POST", "/api/me/header/photo" },
        { "DELETE", "/api/me/header" },
        { "POST", "/api/me/onboarding" },
        { "GET", "/api/me/legacy-tanks" },
        { "GET", "/api/me/legacy-tanks/1/photo-suggestions" },
        { "POST", "/api/me/legacy-tanks/1/adopt" },
        { "GET", "/api/me/retrospectives/archive" },
        { "POST", "/api/me/retrospectives/archive/seen" },
        { "GET", "/api/me/retrospectives/return" },
        { "POST", "/api/me/retrospectives/return/seen" },
        { "PUT", "/api/me/interests" },
        { "PUT", "/api/me/handle" },
        { "PUT", "/api/tanks/1/region" },
        { "GET", "/api/me/web-push" },
        { "POST", "/api/me/web-push/subscriptions" },
        { "DELETE", "/api/me/web-push/subscriptions" },
        { "POST", "/api/me/care/reminders/snooze" },
        { "POST", "/api/me/provisional/preview" },
        { "POST", "/api/me/provisional/upgrade" },
        { "POST", "/api/me/provisional/move" },
    };

    [Theory]
    [MemberData(nameof(GatedRoutes))]
    public async Task GivenAProvisionalCaller_WhenCallingARouteForRegisteredMembers_ThenTheApiAnswersRegistrationRequired(string method, string url)
    {
        using var caller = await NewAsync(fixture.Client);
        await using var db = fixture.CreateDbContext();
        var profiles = await db.Profiles.CountAsync();
        var stories = await db.Stories.CountAsync();
        var follows = await db.Follows.CountAsync();
        var comments = await db.Comments.CountAsync();

        using var response = await SendRouteAsync(method, url, caller.Token);

        await AssertRegistrationRequiredAsync(response);
        Assert.Equal(profiles, await db.Profiles.CountAsync());
        Assert.Equal(stories, await db.Stories.CountAsync());
        Assert.Equal(follows, await db.Follows.CountAsync());
        Assert.Equal(comments, await db.Comments.CountAsync());
    }

    [Theory]
    [MemberData(nameof(GatedRoutes))]
    public async Task GivenAnAnonymousCaller_WhenCallingARouteForRegisteredMembers_ThenTheApiAnswersUnauthorized(string method, string url)
    {
        using var response = await SendRouteAsync(method, url, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendRouteAsync(string method, string url, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (token is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = url.EndsWith("/photo", StringComparison.Ordinal) || url == "/api/uploads"
                ? new MultipartFormDataContent { { new StringContent("x"), "note" } }
                : JsonContent.Create(new { });
        }

        return await fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenAskingToFollowACommentRatingOrPublishOnAVisibleTank_ThenNothingIsStored()
    {
        using var caller = await NewAsync(fixture.Client);
        var owner = await Visibility.NewMemberAsync(fixture);
        var tank = await Visibility.NewTankAsync(fixture, owner.ProfileId, Cichlids.Domain.Enums.TankVisibility.Public, Cichlids.Domain.Enums.TankVisibility.Public);

        using var follow = await SendAsync(fixture.Client, HttpMethod.Put, $"/api/tanks/{tank}/follow", caller.Token);
        using var comment = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/comments", caller.Token, new { body = "Nice tank", stars = 5 });

        await AssertRegistrationRequiredAsync(follow);
        await AssertRegistrationRequiredAsync(comment);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Follows.AnyAsync(f => f.TargetTankId == tank));
        Assert.False(await db.Comments.AnyAsync(c => c.AuthorProfileId == caller.ProfileId));
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenReadingTheFollowStateOfAPublicTank_ThenItReadsLikeAMember()
    {
        using var caller = await NewAsync(fixture.Client);
        var owner = await Visibility.NewMemberAsync(fixture);
        var tank = await Visibility.NewTankAsync(fixture, owner.ProfileId, Cichlids.Domain.Enums.TankVisibility.Public, Cichlids.Domain.Enums.TankVisibility.Public);

        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/follow", caller.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("none", (await BodyAsync(response)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task GivenAProvisionalCaller_WhenDeletingAComment_ThenTheModeratorRoleIsMissing()
    {
        using var caller = await NewAsync(fixture.Client);

        using var response = await SendAsync(fixture.Client, HttpMethod.Delete, "/api/comments/1", caller.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Every route that needs a login is either declared for registered members or listed here as a
    /// route a provisional caller may use. A new authenticated route fails this test until it is
    /// classified.
    /// </summary>
    [Fact]
    public void Every_authenticated_route_is_declared_for_registered_members_or_listed_as_open_to_provisional_callers()
    {
        var routes = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0 && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m =>
                (Route: $"{m} /{e.RoutePattern.RawText!.Trim('/')}",
                 Policies: e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList())))
            .ToList();

        var registered = routes.Where(r => r.Policies.Contains(AuthorizationPolicies.Registered)).Select(r => r.Route).ToHashSet();
        var roles = routes.Where(r => r.Policies.Any(p => p is AuthorizationPolicies.Moderator or AuthorizationPolicies.Admin)).Select(r => r.Route).ToHashSet();
        var open = routes.Select(r => r.Route).Where(r => !registered.Contains(r) && !roles.Contains(r) && r != WebSessionRoute).ToHashSet();

        Assert.Equal(ExpectedOpenRoutes.Order().ToList(), open.Order().ToList());
        Assert.Equal(
            GatedRoutes.Select(g => $"{g[0]} {g[1]}").Order().ToList(),
            registered.Select(WithSampleIds).Order().ToList());
    }

    private static string WithSampleIds(string route)
    {
        var index = 0;
        return System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", _ => (++index).ToString());
    }

    /// <summary>
    /// The routes a provisional caller may use: its own tanks, care, streaks, vacations, media library,
    /// uploads, settings and the sections of Today.
    /// </summary>
    private static readonly string[] ExpectedOpenRoutes =
    [
        "DELETE /api/me/media/{id:long}",
        "DELETE /api/me/todos/{id:long}",
        "DELETE /api/tanks/{tankId:long}/care/{id:long}",
        "DELETE /api/tanks/{tankId:long}/care/{id:long}/photos/{photoId:long}",
        "DELETE /api/tanks/{tankId:long}/care/{id:long}/videos/{mediaItemId:long}",
        "DELETE /api/tanks/{tankId:long}/videos/{mediaItemId:long}",
        "DELETE /api/tanks/{tankId:long}/water-changes/{id:long}",
        "DELETE /api/video-uploads/{id:long}",
        "GET /api/me",
        "GET /api/me/care/due",
        "GET /api/me/care/heatmap",
        "GET /api/me/followed-tanks",
        "GET /api/me/media",
        "GET /api/me/media/{id:long}",
        "GET /api/me/settings",
        "GET /api/me/streaks",
        "GET /api/me/today",
        "GET /api/me/today/followed",
        "GET /api/me/today/milestones",
        "GET /api/me/todos",
        "GET /api/me/vacations",
        "GET /api/tanks/{tankId:long}/water-changes/current",
        "GET /api/video-uploads/{id:long}",
        "PATCH /api/me/todos/{id:long}",
        "PATCH /api/tanks/{tankId:long}/care/plan",
        "PATCH /api/tanks/{tankId:long}/care/plan/{kind}",
        "PATCH /api/tanks/{tankId:long}/care/{id:long}",
        "PATCH /api/video-uploads/{id:long}",
        "POST /api/me/media/photos",
        "POST /api/me/vacations",
        "POST /api/me/vacations/active/end",
        "POST /api/tanks",
        "POST /api/tanks/{tankId:long}/care",
        "POST /api/tanks/{tankId:long}/care/{id:long}/photos",
        "POST /api/tanks/{tankId:long}/care/{id:long}/todos",
        "POST /api/tanks/{tankId:long}/decommissioning",
        "POST /api/tanks/{tankId:long}/decommissioning/end",
        "POST /api/tanks/{tankId:long}/time-lapse/photos",
        "POST /api/tanks/{tankId:long}/todos",
        "POST /api/tanks/{tankId:long}/water-changes",
        "POST /api/tanks/{tankId:long}/water-changes/{id:long}/finish",
        "POST /api/tanks/{tankId:long}/water-changes/{id:long}/photos",
        "POST /api/video-uploads",
        "POST /api/video-uploads/{id:long}/complete",
        "PUT /api/me/settings",
        "PUT /api/me/today/milestones/{key}/dismissal",
        "PUT /api/tanks/{id:long}/visibility",
        "PUT /api/tanks/{tankId:long}/water-changes/{id:long}/steps/{step}",
    ];

    /// <summary>
    /// The route that belongs to the web session; a device token does not open one.
    /// </summary>
    private const string WebSessionRoute = "POST /api/auth/logout";
}
