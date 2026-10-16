using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.WebPush;
using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// Subscribing and unsubscribing a browser for Web Push: owner only, only https endpoints of the
/// allowlisted push services, at most ten subscriptions per member, and an outbox event in the
/// transaction of each change.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.12")]
public sealed class WebPushSubscriptionTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Subscriptions = "/api/me/web-push/subscriptions";
    private readonly List<string> _endpoints = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var rows = await db.WebPushSubscriptions.Where(s => _endpoints.Contains(s.Endpoint)).ToListAsync();
        var ids = rows.Select(r => r.Id.ToString()).ToList();
        await db.OutboxEvents.Where(e => e.AggregateType == "web_push_subscription" && ids.Contains(e.AggregateId)).ExecuteDeleteAsync();
        db.WebPushSubscriptions.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private FakeBrowser NewBrowser(string? endpoint = null)
    {
        var browser = new FakeBrowser(endpoint ?? FakeBrowser.NewEndpoint());
        _endpoints.Add(browser.Endpoint);
        return browser;
    }

    [Fact]
    public async Task GivenAMember_WhenReadingTheConfiguration_ThenThePublicKeyIsServed()
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/web-push", NewMemberToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.True(body.GetProperty("available").GetBoolean());
        Assert.Equal(ApiFixture.VapidKeyPair.PublicKey, body.GetProperty("publicKey").GetString());
        Assert.False(body.TryGetProperty("privateKey", out _));
    }

    [Fact]
    public async Task GivenABrowser_WhenSubscribing_ThenTheSubscriptionIsStoredWithItsTimeZoneAndOneEventIsRecorded()
    {
        var token = NewMemberToken();
        using var browser = NewBrowser();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody("Pacific/Auckland"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await BodyAsync(response)).GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var row = await db.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.Equal(browser.Endpoint, row.Endpoint);
        Assert.Equal(browser.P256dh, row.P256dh);
        Assert.Equal(browser.Auth, row.Auth);
        Assert.Equal("Pacific/Auckland", row.TimeZone);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.LastSuccessAt);
        var events = await EventsAsync(db, "web_push_subscription", id);
        var created = Assert.Single(events);
        Assert.Equal("web_push.subscribed", created.Type);
        Assert.Equal(id, created.Payload.GetProperty("subscriptionId").GetInt64());
        Assert.Equal("Pacific/Auckland", created.Payload.GetProperty("timeZone").GetString());
        Assert.DoesNotContain(browser.Endpoint, created.Payload.GetRawText());
        Assert.DoesNotContain(browser.Auth, created.Payload.GetRawText());
    }

    [Fact]
    public async Task GivenASubscribedBrowser_WhenSubscribingAgain_ThenTheRowIsUpdatedInPlaceWithoutANewEvent()
    {
        var token = NewMemberToken();
        using var browser = NewBrowser();
        using var first = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody("Europe/Berlin"));
        var id = (await BodyAsync(first)).GetProperty("id").GetInt64();

        using var again = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody("America/New_York"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(id, (await BodyAsync(again)).GetProperty("id").GetInt64());
        await using var db = fixture.CreateDbContext();
        Assert.Equal("America/New_York", (await db.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == browser.Endpoint)).TimeZone);
        Assert.Single(await EventsAsync(db, "web_push_subscription", id));
    }

    [Fact]
    public async Task GivenABrowserOfAnotherMember_WhenSubscribingWithItsEndpoint_ThenTheSubscriptionMovesToTheCaller()
    {
        var first = NewMemberToken();
        var second = NewMemberToken();
        using var browser = NewBrowser();
        using var firstResponse = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, first, browser.SubscribeBody());
        var firstId = (await BodyAsync(firstResponse)).GetProperty("id").GetInt64();

        using var secondResponse = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, second, browser.SubscribeBody());

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        await using var db = fixture.CreateDbContext();
        var row = await db.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == browser.Endpoint);
        Assert.NotEqual(firstId, row.Id);
        Assert.Contains(await EventsAsync(db, "web_push_subscription", firstId), e => e.Type == "web_push.unsubscribed");
    }

    [Fact]
    public async Task GivenTwoMembersSubscribingOneEndpointAtOnce_WhenBothRequestsRun_ThenNeitherFailsAndOneRowRemains()
    {
        using var browser = NewBrowser();
        for (var round = 0; round < 5; round++)
        {
            var tokens = Enumerable.Range(0, 4).Select(_ => NewMemberToken()).ToList();
            var responses = await Task.WhenAll(tokens.Select(token =>
                SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody())));
            try
            {
                Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"status {(int)r.StatusCode}"));
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.WebPushSubscriptions.CountAsync(s => s.Endpoint == browser.Endpoint));
    }

    [Theory]
    [InlineData("https://evil.example.test/send/abc")]
    [InlineData("http://push.example.test/send/abc")]
    [InlineData("https://push.example.test.evil.test/send/abc")]
    [InlineData("https://127.0.0.1/send/abc")]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://user:pw@push.example.test/send/abc")]
    [InlineData("https://push.example.test:8443/send/abc")]
    public async Task GivenAnEndpointOutsideTheAllowlist_WhenSubscribing_ThenItIsRefusedAndNothingIsStored(string endpoint)
    {
        using var browser = NewBrowser(endpoint);

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, NewMemberToken(), browser.SubscribeBody());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "web_push_endpoint_not_allowed");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebPushSubscriptions.AnyAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task GivenASubdomainOfAnAllowedPushService_WhenSubscribing_ThenItIsAccepted()
    {
        using var browser = NewBrowser(FakeBrowser.NewEndpoint("eu.push.example.test"));

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, NewMemberToken(), browser.SubscribeBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static TheoryData<object> InvalidBodies() => new()
    {
        new { endpoint = (string?)null, keys = new { p256dh = "x", auth = "y" }, timeZone = "Europe/Berlin" },
        new { endpoint = "https://push.example.test/a", keys = new { p256dh = "AAAA", auth = "AAAAAAAAAAAAAAAAAAAAAA" }, timeZone = "Europe/Berlin" },
        new { endpoint = "https://push.example.test/a", keys = new { p256dh = "not base64 !", auth = "AAAAAAAAAAAAAAAAAAAAAA" }, timeZone = "Europe/Berlin" },
        new { endpoint = "https://push.example.test/a", keys = (object?)null, timeZone = "Europe/Berlin" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task GivenInvalidSubscriptionData_WhenSubscribing_ThenACodedProblemIsReturned(object body)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, NewMemberToken(), body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "web_push_subscription_invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus")]
    [InlineData("../../etc/passwd")]
    public async Task GivenAnUnknownTimeZone_WhenSubscribing_ThenACodedProblemIsReturned(string timeZone)
    {
        using var browser = NewBrowser();

        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, NewMemberToken(), browser.SubscribeBody(timeZone));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "web_push_subscription_invalid");
    }

    [Fact]
    public async Task GivenTenSubscriptions_WhenSubscribingAnEleventhBrowser_ThenTheLimitIsReachedAndTheTenthStaysUpdatable()
    {
        var token = NewMemberToken();
        var browsers = Enumerable.Range(0, 10).Select(_ => NewBrowser()).ToList();
        foreach (var browser in browsers)
        {
            using var ok = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody());
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        using var eleventh = NewBrowser();
        using var refused = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, eleventh.SubscribeBody());
        using var update = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browsers[9].SubscribeBody("Asia/Tokyo"));

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "web_push_limit_reached");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        foreach (var browser in browsers) browser.Dispose();
    }

    [Fact]
    public async Task GivenASubscription_WhenUnsubscribing_ThenItIsDeletedAndOneEventIsRecorded()
    {
        var token = NewMemberToken();
        using var browser = NewBrowser();
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, token, browser.SubscribeBody());
        var id = (await BodyAsync(created)).GetProperty("id").GetInt64();

        using var response = await SendAsync(fixture.Client, HttpMethod.Delete, Subscriptions, token, new { endpoint = browser.Endpoint });
        using var again = await SendAsync(fixture.Client, HttpMethod.Delete, Subscriptions, token, new { endpoint = browser.Endpoint });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebPushSubscriptions.AnyAsync(s => s.Endpoint == browser.Endpoint));
        var removed = (await EventsAsync(db, "web_push_subscription", id)).Where(e => e.Type == "web_push.unsubscribed").ToList();
        Assert.Single(removed);
        Assert.DoesNotContain(browser.Endpoint, removed[0].Payload.GetRawText());
    }

    [Fact]
    public async Task GivenAnotherMembersSubscription_WhenUnsubscribingItsEndpoint_ThenItStays()
    {
        var owner = NewMemberToken();
        using var browser = NewBrowser();
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, Subscriptions, owner, browser.SubscribeBody());

        using var response = await SendAsync(fixture.Client, HttpMethod.Delete, Subscriptions, NewMemberToken(), new { endpoint = browser.Endpoint });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True(await db.WebPushSubscriptions.AnyAsync(s => s.Endpoint == browser.Endpoint));
    }

    [Fact]
    public async Task GivenNoLogin_WhenUsingTheWebPushEndpoints_ThenACodedAuthProblemIsReturned()
    {
        using var browser = NewBrowser();
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, "/api/me/web-push", null),
            (HttpMethod.Post, Subscriptions, browser.SubscribeBody()),
            (HttpMethod.Delete, Subscriptions, new { endpoint = browser.Endpoint }),
        })
        {
            using var response = await SendAsync(fixture.Client, method, url, null, body);
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "web_push_auth_required");
        }
    }

    [Fact]
    public async Task GivenAMalformedBody_WhenSubscribing_ThenACodedProblemIsReturned()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Subscriptions) { Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NewMemberToken());

        using var response = await fixture.Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "web_push_request_invalid");
    }

    [Fact]
    public async Task GivenWebPushIsDisabled_WhenSubscribing_ThenTheServiceRefusesWithACode()
    {
        await using var db = fixture.CreateDbContext();
        var disabled = WebPushSettings.Resolve(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), "Production", "/srv");
        var service = new WebPushSubscriptionService(db, disabled, TimeProvider.System);
        using var browser = NewBrowser();

        var (subscription, _, error) = await service.SubscribeAsync(1, new SubscribeWebPushRequest(browser.Endpoint,
            new WebPushKeysRequest(browser.P256dh, browser.Auth), "Europe/Berlin"), CancellationToken.None);

        Assert.Null(subscription);
        Assert.Equal("web_push_unavailable", error);
    }
}
