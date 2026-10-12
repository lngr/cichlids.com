using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Webhooks;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// Administrator access to given-up outbox events and their redelivery. Each test starts from an
/// empty outbox and no subscriptions in the shared dispatch database.
/// </summary>
[Collection(OutboxDispatchCollection.Name)]
public class OutboxAdminEndpointsTests(OutboxDispatchFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetOutboxAsync();

    public Task DisposeAsync() => fixture.ResetOutboxAsync();

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/admin/outbox/events?state=given-up" },
        { "POST", $"/api/admin/outbox/events/{Guid.NewGuid()}/redeliver" },
        { "POST", "/api/admin/outbox/given-up/redeliver" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_AnonymousReturnsUnauthorized(string method, string path)
    {
        var response = await SendAsync(method, path, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_WithoutAdminRoleReturnsForbidden(string method, string path)
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"plain-{Guid.NewGuid():N}"[..24]);

        var response = await SendAsync(method, path, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_ModeratorRoleAloneReturnsForbidden(string method, string path)
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), $"mod-{Guid.NewGuid():N}"[..24], roles: ["moderator"]);

        var response = await SendAsync(method, path, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Redeliver_UnknownEventReturnsNotFound()
    {
        var response = await SendAsync("POST", $"/api/admin/outbox/events/{Guid.NewGuid()}/redeliver", AdminToken());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Redeliver_PendingOrDispatchedEventReturnsConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await InsertEventAsync(now);
        var dispatched = await InsertEventAsync(now, e => e.DispatchedAt = now);

        var pendingResponse = await SendAsync("POST", $"/api/admin/outbox/events/{pending}/redeliver", AdminToken());
        var dispatchedResponse = await SendAsync("POST", $"/api/admin/outbox/events/{dispatched}/redeliver", AdminToken());

        Assert.Equal(HttpStatusCode.Conflict, pendingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dispatchedResponse.StatusCode);
    }

    [Fact]
    public async Task ListEvents_GivenUpListsOnlyGivenUpEventsWithTheirError()
    {
        var now = DateTimeOffset.UtcNow;
        var givenUp = await InsertGivenUpEventAsync(now.AddDays(-8), "subscription 7 responded 503");
        await InsertEventAsync(now);
        await InsertEventAsync(now, e => e.DispatchedAt = now);

        var response = await SendAsync("GET", "/api/admin/outbox/events?state=given-up", AdminToken());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = JsonSerializer.Deserialize<PagedResponse<OutboxEventDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal(givenUp, item.Id);
        Assert.Equal("test.event", item.EventType);
        Assert.NotNull(item.GivenUpAt);
        Assert.Equal("subscription 7 responded 503", item.LastError);
    }

    [Fact]
    public async Task ListEvents_PagesWithOffsetAndLimit()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            await InsertGivenUpEventAsync(now.AddDays(-8).AddMinutes(i), "gave up");
        }

        var response = await SendAsync("GET", "/api/admin/outbox/events?state=given-up&offset=1&limit=1", AdminToken());

        var page = JsonSerializer.Deserialize<PagedResponse<OutboxEventDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task ListEvents_UnknownStateReturnsBadRequest()
    {
        var response = await SendAsync("GET", "/api/admin/outbox/events?state=everything", AdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Redeliver_GivenUpEventIsDeliveredWithTheSameIdAndAValidSignature()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        const string secret = "redeliver-secret"; // gitleaks:allow
        await CreateSubscriptionAsync(listener.Uri, secret, ["test.event"]);
        var id = await InsertGivenUpEventAsync(DateTimeOffset.UtcNow.AddDays(-8), "subscription 1 responded 503");

        var response = await SendAsync("POST", $"/api/admin/outbox/events/{id}/redeliver", AdminToken());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var scheduled = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Null(scheduled.GivenUpAt);
            Assert.Equal(0, scheduled.AttemptCount);
            Assert.Null(scheduled.LastError);
            Assert.NotNull(scheduled.RedeliveredAt);
            Assert.Equal(scheduled.RedeliveredAt, scheduled.NextAttemptAt);
        }

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        var request = Assert.Single(listener.AcceptedRequests);
        Assert.Equal(id, request.EventId);
        var expectedSignature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(request.Body)));
        Assert.Equal(expectedSignature, request.Signature);

        await using var verify = fixture.CreateDbContext();
        Assert.NotNull((await verify.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id)).DispatchedAt);
    }

    [Fact]
    public async Task RedeliverAllGivenUp_SchedulesEveryGivenUpEvent()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "bulk-secret", ["test.event"]);
        var now = DateTimeOffset.UtcNow;
        var first = await InsertGivenUpEventAsync(now.AddDays(-9), "gave up");
        var second = await InsertGivenUpEventAsync(now.AddDays(-8), "gave up");
        var dispatched = await InsertEventAsync(now, e => e.DispatchedAt = now);

        var response = await SendAsync("POST", "/api/admin/outbox/given-up/redeliver", AdminToken());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = JsonSerializer.Deserialize<OutboxRedeliveryDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.Equal(2, result.Count);

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { first, second }.Order(), listener.AcceptedRequests.Select(r => r.EventId).Order());
        Assert.DoesNotContain(dispatched, listener.Requests.Select(r => r.EventId));
    }

    [Fact]
    public async Task Redeliver_EventThatFailsAgainIsRetriedUntilTheNewPeriodEnds()
    {
        await using var listener = new TestWebhookListener();
        listener.SetDown(true);
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "window-secret", ["test.event"]);
        var id = await InsertGivenUpEventAsync(DateTimeOffset.UtcNow.AddDays(-30), "gave up");

        var response = await SendAsync("POST", $"/api/admin/outbox/events/{id}/redeliver", AdminToken());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        DateTimeOffset redeliveredAt;
        await using (var db = fixture.CreateDbContext())
        {
            redeliveredAt = (await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id)).RedeliveredAt!.Value;
        }

        var options = new OutboxDispatcherOptions { GiveUpAfter = TimeSpan.FromHours(1) };
        var clock = new FakeTimeProvider(redeliveredAt.AddSeconds(1));
        var dispatcher = fixture.CreateDispatchService(options, clock);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(30));
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await AssertGivenUpAsync(id, expected: false);

        clock.Advance(TimeSpan.FromMinutes(31));
        await dispatcher.RunOnceAsync(CancellationToken.None);
        await AssertGivenUpAsync(id, expected: true);
        Assert.Equal(3, listener.Requests.Count);
    }

    [Fact]
    public async Task Redeliver_GoesOnlyToSubscribersThatHaveNotAcknowledged()
    {
        await using var acknowledged = new TestWebhookListener();
        acknowledged.Start();
        await using var missing = new TestWebhookListener();
        missing.Start();
        var acknowledgedId = await CreateSubscriptionAsync(acknowledged.Uri, "acknowledged-secret", ["test.event"]);
        await CreateSubscriptionAsync(missing.Uri, "missing-secret", ["test.event"]);
        var id = await InsertGivenUpEventAsync(
            DateTimeOffset.UtcNow.AddDays(-8), "gave up", e => e.AcknowledgedSubscriptionIds = [acknowledgedId]);

        var response = await SendAsync("POST", $"/api/admin/outbox/events/{id}/redeliver", AdminToken());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Empty(acknowledged.Requests);
        Assert.Equal([id], missing.AcceptedRequests.Select(r => r.EventId));
    }

    private async Task AssertGivenUpAsync(Guid id, bool expected)
    {
        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(expected, outboxEvent.GivenUpAt is not null);
        Assert.Null(outboxEvent.DispatchedAt);
    }

    private static string AdminToken() =>
        TestTokens.Create(Guid.NewGuid().ToString(), $"admin-{Guid.NewGuid():N}"[..24], roles: ["admin"]);

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await fixture.Client.SendAsync(request);
    }

    private async Task<long> CreateSubscriptionAsync(Uri url, string secret, IReadOnlyList<string> eventTypes)
    {
        await using var db = fixture.CreateDbContext();
        var subscription = new WebhookSubscription
        {
            Url = url.ToString(),
            Secret = secret,
            EventTypes = eventTypes.ToList(),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private Task<Guid> InsertGivenUpEventAsync(
        DateTimeOffset occurredAt, string lastError, Action<OutboxEvent>? configure = null) =>
        InsertEventAsync(occurredAt, e =>
        {
            e.AttemptCount = 170;
            e.LastError = lastError;
            e.LastAttemptAt = occurredAt.AddDays(7);
            e.GivenUpAt = occurredAt.AddDays(7);
            configure?.Invoke(e);
        });

    private async Task<Guid> InsertEventAsync(DateTimeOffset occurredAt, Action<OutboxEvent>? configure = null)
    {
        await using var db = fixture.CreateDbContext();
        var outboxEvent = OutboxEvent.Create("test.event", "post", "1", """{"postId":1}""", occurredAt);
        configure?.Invoke(outboxEvent);
        db.OutboxEvents.Add(outboxEvent);
        await db.SaveChangesAsync();
        return outboxEvent.Id;
    }
}
