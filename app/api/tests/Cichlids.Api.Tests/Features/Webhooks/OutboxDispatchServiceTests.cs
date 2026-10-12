using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// Every test runs dispatch passes against the fixture's single shared Postgres container, so an
/// open event or a subscription left behind by one test would be delivered or matched in a later
/// one. Each test therefore starts from an empty outbox and no subscriptions.
/// </summary>
[Collection(OutboxDispatchCollection.Name)]
public class OutboxDispatchServiceTests(OutboxDispatchFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetOutboxAsync();

    public Task DisposeAsync() => fixture.ResetOutboxAsync();

    [Fact]
    public async Task RunOnceAsync_DeliversDueEventExactlyOnceWithAValidSignature()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();

        const string secret = "matching-subscriber-secret"; // gitleaks:allow
        await CreateSubscriptionAsync(listener.Uri, secret, ["comment.created"]);

        var (postId, slug) = await CreatePublishedPictureAsync();
        var commentId = await PostCommentAsync(slug);

        var dispatched = await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, dispatched);

        Assert.Single(listener.Requests);
        var request = listener.Requests[0];

        var expectedSignature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(request.Body)));
        Assert.Equal(expectedSignature, request.Signature);

        using var envelope = JsonDocument.Parse(request.Body);
        Assert.Equal("comment.created", envelope.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("post", envelope.RootElement.GetProperty("aggregateType").GetString());
        Assert.Equal(postId.ToString(), envelope.RootElement.GetProperty("aggregateId").GetString());
        Assert.Equal(commentId, envelope.RootElement.GetProperty("payload").GetProperty("commentId").GetInt64());

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.SingleAsync(e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
        Assert.NotNull(outboxEvent.DispatchedAt);
        Assert.Equal(0, outboxEvent.AttemptCount);
    }

    [Fact]
    public async Task RunOnceAsync_NoMatchingSubscriberStillMarksTheEventDispatched()
    {
        var (postId, slug) = await CreatePublishedPictureAsync();
        await PostCommentAsync(slug);

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.SingleAsync(e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
        Assert.NotNull(outboxEvent.DispatchedAt);
    }

    [Fact]
    public async Task RunOnceAsync_FailingSubscriberRetriesAndLaterSucceeds()
    {
        await using var listener = new TestWebhookListener();
        listener.FailNextDeliveries(1);
        listener.Start();

        await CreateSubscriptionAsync(listener.Uri, "retry-secret", ["comment.created"]);

        var (postId, slug) = await CreatePublishedPictureAsync();
        await PostCommentAsync(slug);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var dispatcher = fixture.CreateDispatchService(timeProvider: clock);
        await dispatcher.RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            var afterFirstAttempt = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
            Assert.Null(afterFirstAttempt.DispatchedAt);
            Assert.Equal(1, afterFirstAttempt.AttemptCount);
            Assert.Contains("responded 500", afterFirstAttempt.LastError);
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        await dispatcher.RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            var afterRetry = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
            Assert.NotNull(afterRetry.DispatchedAt);
            Assert.Equal(1, afterRetry.AttemptCount);
            Assert.Null(afterRetry.LastError);
            Assert.Equal(afterRetry.DispatchedAt, afterRetry.LastAttemptAt);
        }

        Assert.Equal(2, listener.Requests.Count);
    }

    [Fact]
    public async Task RunOnceAsync_OnlyDueEventsAreSelected()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "due-secret", ["test.event"]);

        var options = new OutboxDispatcherOptions();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < options.BatchSize + 5; i++)
        {
            await InsertEventAsync(occurredAt: now.AddHours(-3).AddSeconds(i), nextAttemptAt: now.AddMinutes(30));
        }

        var due = await InsertEventAsync(occurredAt: now.AddMinutes(-1));

        var reserved = await fixture.CreateDispatchService(options, new FakeTimeProvider(now)).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, reserved);
        Assert.Equal([due], listener.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task RunOnceAsync_BackoffCountsFromTheLastAttempt()
    {
        await using var listener = new TestWebhookListener();
        listener.FailNextDeliveries(1);
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "backoff-secret", ["test.event"]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var id = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddHours(-3));
        var dispatcher = fixture.CreateDispatchService(timeProvider: clock);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.Single(listener.Requests);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.Single(listener.Requests);

        clock.Advance(TimeSpan.FromSeconds(5));
        await dispatcher.RunOnceAsync(CancellationToken.None);

        Assert.Equal([id], listener.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task RunOnceAsync_SubscriberDownForTwoHoursReceivesEventsPublishedMeanwhile()
    {
        await using var listener = new TestWebhookListener();
        listener.SetDown(true);
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "outage-secret", ["test.event"]);

        var options = new OutboxDispatcherOptions();
        var t0 = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(t0);
        var dispatcher = fixture.CreateDispatchService(options, clock);
        var published = new List<Guid>();
        var lastAttempts = new Dictionary<Guid, DateTimeOffset?>();

        // The clock runs well past the expected delivery, so a late delivery shows up in the bound
        // asserted below.
        for (var minute = 0; minute <= 300; minute++)
        {
            if (minute is 0 or 40 or 100)
            {
                published.Add(await InsertEventAsync(occurredAt: clock.GetUtcNow()));
            }

            if (minute == 120)
            {
                listener.SetDown(false);
            }

            await dispatcher.RunOnceAsync(CancellationToken.None);

            await using var db = fixture.CreateDbContext();
            foreach (var outboxEvent in await db.OutboxEvents.AsNoTracking().ToListAsync())
            {
                Assert.Null(outboxEvent.GivenUpAt);
                if (outboxEvent.DispatchedAt is null && outboxEvent.LastAttemptAt is { } lastAttemptAt
                    && lastAttempts.GetValueOrDefault(outboxEvent.Id) != lastAttemptAt)
                {
                    var expectedDelay = OutboxBackoff.Compute(outboxEvent.AttemptCount, options.MaxBackoff);
                    Assert.Equal(expectedDelay, outboxEvent.NextAttemptAt - lastAttemptAt);
                    lastAttempts[outboxEvent.Id] = lastAttemptAt;
                }
            }

            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(published.Order(), listener.AcceptedRequests.Select(r => r.EventId).Order());
        Assert.All(listener.AcceptedRequests, r => Assert.True(
            listener.Requests.Count(other => other.Succeeded && other.EventId == r.EventId) == 1));

        await using var verify = fixture.CreateDbContext();
        var events = await verify.OutboxEvents.AsNoTracking().ToListAsync();
        Assert.All(events, e =>
        {
            Assert.NotNull(e.DispatchedAt);
            Assert.Null(e.GivenUpAt);
            Assert.True(
                e.DispatchedAt <= t0 + TimeSpan.FromHours(2) + options.MaxBackoff,
                $"Event {e.Id} was dispatched at {e.DispatchedAt}, later than one maximum backoff after the subscriber came back.");
        });
    }

    [Fact]
    public async Task RunOnceAsync_NoLockIsHeldDuringHttp()
    {
        await using var listener = new TestWebhookListener();
        listener.HoldRequests();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "lock-secret", ["test.event"]);

        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        var pass = fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        try
        {
            await listener.RequestArrived.WaitAsync(TimeSpan.FromSeconds(10));

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT id FROM outbox_event WHERE id = @id FOR UPDATE NOWAIT", connection, transaction);
            command.Parameters.AddWithValue("id", id);
            Assert.Equal(id, await command.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
        finally
        {
            listener.Release();
            await pass;
        }

        Assert.Equal([id], listener.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task RunOnceAsync_ExpiredLeaseIsReservedByAnotherInstance()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "lease-secret", ["test.event"]);

        var options = new OutboxDispatcherOptions();
        var t0 = DateTimeOffset.UtcNow;
        var id = await InsertEventAsync(occurredAt: t0.AddSeconds(-1));

        var crashed = await fixture.CreateDispatchService(options, new FakeTimeProvider(t0)).ReserveDueAsync(CancellationToken.None);
        Assert.Equal([id], crashed.Select(e => e.Id));

        var beforeExpiry = fixture.CreateDispatchService(options, new FakeTimeProvider(t0 + options.ReservationLease - TimeSpan.FromSeconds(1)));
        Assert.Equal(0, await beforeExpiry.RunOnceAsync(CancellationToken.None));

        var afterExpiry = fixture.CreateDispatchService(options, new FakeTimeProvider(t0 + options.ReservationLease + TimeSpan.FromSeconds(1)));
        Assert.Equal(1, await afterExpiry.RunOnceAsync(CancellationToken.None));

        Assert.Equal([id], listener.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task RunOnceAsync_StaleResultIsDiscarded()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "stale-secret", ["test.event"]);

        var options = new OutboxDispatcherOptions();
        var t0 = DateTimeOffset.UtcNow;
        var id = await InsertEventAsync(occurredAt: t0.AddSeconds(-1));

        var slow = fixture.CreateDispatchService(options, new FakeTimeProvider(t0));
        var reservedBySlow = await slow.ReserveDueAsync(CancellationToken.None);

        var other = fixture.CreateDispatchService(options, new FakeTimeProvider(t0 + options.ReservationLease + TimeSpan.FromSeconds(1)));
        Assert.Equal(1, await other.RunOnceAsync(CancellationToken.None));

        listener.SetDown(true);
        await slow.ProcessAsync(reservedBySlow, CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.NotNull(outboxEvent.DispatchedAt);
        Assert.Equal(0, outboxEvent.AttemptCount);
        Assert.Null(outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_LateSuccessAfterAFailedTakeoverIsDiscarded()
    {
        await using var listener = new TestWebhookListener();
        listener.SetDown(true);
        listener.Start();
        var subscriptionId = await CreateSubscriptionAsync(listener.Uri, "late-secret", ["test.event"]);

        var options = new OutboxDispatcherOptions();
        var t0 = DateTimeOffset.UtcNow;
        var id = await InsertEventAsync(occurredAt: t0.AddSeconds(-1));

        var slow = fixture.CreateDispatchService(options, new FakeTimeProvider(t0));
        var reservedBySlow = await slow.ReserveDueAsync(CancellationToken.None);

        var takeoverAt = t0 + options.ReservationLease + TimeSpan.FromSeconds(1);
        var other = fixture.CreateDispatchService(options, new FakeTimeProvider(takeoverAt));
        Assert.Equal(1, await other.RunOnceAsync(CancellationToken.None));

        DateTimeOffset nextAttemptAfterTakeover;
        await using (var db = fixture.CreateDbContext())
        {
            var afterTakeover = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Null(afterTakeover.DispatchedAt);
            Assert.Equal(1, afterTakeover.AttemptCount);
            nextAttemptAfterTakeover = afterTakeover.NextAttemptAt;
        }

        listener.SetDown(false);
        await slow.ProcessAsync(reservedBySlow, CancellationToken.None);

        Assert.Single(listener.AcceptedRequests);
        await using (var db = fixture.CreateDbContext())
        {
            var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Null(outboxEvent.DispatchedAt);
            Assert.Empty(outboxEvent.AcknowledgedSubscriptionIds);
            Assert.Equal(1, outboxEvent.AttemptCount);
            Assert.Equal(nextAttemptAfterTakeover, outboxEvent.NextAttemptAt);
            Assert.Equal($"subscription {subscriptionId} responded 503", outboxEvent.LastError);
        }
    }

    [Fact]
    public async Task RunOnceAsync_ConcurrentDispatchersDeliverEachEventExactlyOnce()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "concurrent-secret", ["test.event"]);

        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < 60; i++)
        {
            ids.Add(await InsertEventAsync(occurredAt: now.AddSeconds(-60 + i)));
        }

        var instances = Enumerable.Range(0, 3).Select(_ => fixture.CreateDispatchService()).ToList();
        await Task.WhenAll(instances.Select(instance => Task.Run(async () =>
        {
            while (await instance.RunOnceAsync(CancellationToken.None) > 0)
            {
            }
        })));

        var delivered = listener.Requests.Select(r => r.EventId).ToList();
        Assert.Equal(ids.Order(), delivered.Order());

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.OutboxEvents.AnyAsync(e => e.DispatchedAt == null));
    }

    [Fact]
    public async Task RunOnceAsync_CorruptPayloadIsAFailedAttemptAndOthersAreDelivered()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await CreateSubscriptionAsync(listener.Uri, "corrupt-secret", ["test.event"]);

        var now = DateTimeOffset.UtcNow;
        var corrupt = await InsertCorruptEventAsync(now.AddSeconds(-2));
        var good = await InsertEventAsync(occurredAt: now.AddSeconds(-1));

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Equal([good], listener.AcceptedRequests.Select(r => r.EventId));

        await using var db = fixture.CreateDbContext();
        var corruptEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == corrupt);
        Assert.Null(corruptEvent.DispatchedAt);
        Assert.Equal(1, corruptEvent.AttemptCount);
        Assert.NotNull(corruptEvent.LastError);
        Assert.NotNull((await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == good)).DispatchedAt);
    }

    [Theory]
    [InlineData("ftp://127.0.0.1/hook")]
    [InlineData("http://169.254.169.254/latest")]
    public async Task RunOnceAsync_SubscriptionOutsideTheAllowlistIsAFailedAttempt(string url)
    {
        var subscriptionId = await CreateSubscriptionAsync(new Uri(url), "stored-secret", ["test.event"]);
        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Null(outboxEvent.DispatchedAt);
        Assert.Equal(1, outboxEvent.AttemptCount);
        Assert.Equal($"subscription {subscriptionId} target is not allowed", outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_RedirectIsNotFollowed()
    {
        await using var target = new TestWebhookListener();
        target.Start();
        await using var redirecting = new TestWebhookListener();
        redirecting.RespondWith(HttpStatusCode.Found, target.Uri);
        redirecting.Start();
        await CreateSubscriptionAsync(redirecting.Uri, "redirect-secret", ["test.event"]);

        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Single(redirecting.Requests);
        Assert.Empty(target.Requests);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Null(outboxEvent.DispatchedAt);
        Assert.Contains("302", outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_SlowSubscriberTimesOut()
    {
        await using var listener = new TestWebhookListener();
        listener.HoldRequests();
        listener.Start();
        var subscriptionId = await CreateSubscriptionAsync(listener.Uri, "slow-secret", ["test.event"]);

        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        var options = new OutboxDispatcherOptions { DeliveryTimeout = TimeSpan.FromSeconds(1) };

        var pass = fixture.CreateDispatchService(options).RunOnceAsync(CancellationToken.None);
        var finished = await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(8)));
        listener.Release();

        Assert.Same(pass, finished);
        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Null(outboxEvent.DispatchedAt);
        Assert.Equal(1, outboxEvent.AttemptCount);
        Assert.Equal($"subscription {subscriptionId} timed out", outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_LastErrorDoesNotContainTheUrl()
    {
        await using var listener = new TestWebhookListener();
        listener.SetDown(true);
        listener.Start();
        var subscriptionId = await CreateSubscriptionAsync(listener.Uri, "url-secret", ["test.event"]);

        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal($"subscription {subscriptionId} responded 503", outboxEvent.LastError);
        Assert.DoesNotContain(listener.Uri.Authority, outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_AcknowledgedSubscriberIsNotCalledAgainWhileAnotherIsDown()
    {
        await using var healthy = new TestWebhookListener();
        healthy.Start();
        await using var down = new TestWebhookListener();
        down.SetDown(true);
        down.Start();
        var healthyId = await CreateSubscriptionAsync(healthy.Uri, "healthy-secret", ["test.event"]);
        var downId = await CreateSubscriptionAsync(down.Uri, "down-secret", ["test.event"]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var id = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddSeconds(-1));
        var dispatcher = fixture.CreateDispatchService(timeProvider: clock);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));
        await dispatcher.RunOnceAsync(CancellationToken.None);

        Assert.Single(healthy.Requests);
        Assert.Equal(2, down.Requests.Count);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Null(outboxEvent.DispatchedAt);
        Assert.Equal([healthyId], outboxEvent.AcknowledgedSubscriptionIds);
        Assert.Equal(2, outboxEvent.AttemptCount);
        Assert.Equal($"subscription {downId} responded 503", outboxEvent.LastError);
    }

    [Fact]
    public async Task RunOnceAsync_OneFailingSubscriberDoesNotBlockAnother()
    {
        await using var down = new TestWebhookListener();
        down.SetDown(true);
        down.Start();
        await using var healthy = new TestWebhookListener();
        healthy.Start();
        await CreateSubscriptionAsync(down.Uri, "down-first-secret", ["test.event"]);
        await CreateSubscriptionAsync(healthy.Uri, "healthy-second-secret", ["test.event"]);

        var id = await InsertEventAsync(occurredAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Single(down.Requests);
        Assert.Equal([id], healthy.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task RunOnceAsync_EventIsDispatchedOnceEveryMatchingSubscriberAcknowledged()
    {
        await using var first = new TestWebhookListener();
        first.Start();
        await using var second = new TestWebhookListener();
        second.SetDown(true);
        second.Start();
        var firstId = await CreateSubscriptionAsync(first.Uri, "first-secret", ["test.event"]);
        var secondId = await CreateSubscriptionAsync(second.Uri, "second-secret", ["*"]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var id = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddSeconds(-1));
        var dispatcher = fixture.CreateDispatchService(timeProvider: clock);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null((await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id)).DispatchedAt);
        }

        second.SetDown(false);
        clock.Advance(TimeSpan.FromSeconds(5));
        await dispatcher.RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            var outboxEvent = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.NotNull(outboxEvent.DispatchedAt);
            Assert.Equal([firstId, secondId], outboxEvent.AcknowledgedSubscriptionIds.Order());
        }

        Assert.Single(first.AcceptedRequests);
        Assert.Single(second.AcceptedRequests);
    }

    [Fact]
    public async Task RunOnceAsync_EventWithMoreSubscribersThanTheLeaseAllowsMakesProgressAndDoesNotBlockOthers()
    {
        // A lease of three delivery timeouts leaves room for two deliveries per event in a pass.
        var options = new OutboxDispatcherOptions
        {
            DeliveryTimeout = TimeSpan.FromSeconds(1),
            ReservationLease = TimeSpan.FromSeconds(3),
        };
        var listeners = new List<TestWebhookListener>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var listener = new TestWebhookListener();
                listener.Start();
                listeners.Add(listener);
                await CreateSubscriptionAsync(listener.Uri, $"wide-secret-{i}", ["test.event"]);
            }

            await using var other = new TestWebhookListener();
            other.Start();
            await CreateSubscriptionAsync(other.Uri, "other-secret", ["other.event"]);

            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var wide = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddSeconds(-2));
            var coReserved = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddSeconds(-1), eventType: "other.event");
            var dispatcher = fixture.CreateDispatchService(options, clock);

            await dispatcher.RunOnceAsync(CancellationToken.None);

            Assert.Equal(2, listeners.Sum(l => l.AcceptedRequests.Count));
            Assert.Equal([coReserved], other.AcceptedRequests.Select(r => r.EventId));
            await using (var db = fixture.CreateDbContext())
            {
                var afterFirstPass = await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == wide);
                Assert.Null(afterFirstPass.DispatchedAt);
                Assert.Equal(1, afterFirstPass.AttemptCount);
                Assert.Equal(2, afterFirstPass.AcknowledgedSubscriptionIds.Count);
                Assert.Contains("not attempted", afterFirstPass.LastError);
                Assert.NotNull((await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == coReserved)).DispatchedAt);
            }

            clock.Advance(TimeSpan.FromSeconds(5));
            await dispatcher.RunOnceAsync(CancellationToken.None);

            Assert.All(listeners, l => Assert.Equal([wide], l.AcceptedRequests.Select(r => r.EventId)));
            await using (var db = fixture.CreateDbContext())
            {
                Assert.NotNull((await db.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == wide)).DispatchedAt);
            }
        }
        finally
        {
            foreach (var listener in listeners)
            {
                await listener.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task RunOnceAsync_SubscribersBeyondTheLeaseAreReachedOnLaterAttempts()
    {
        var options = new OutboxDispatcherOptions
        {
            DeliveryTimeout = TimeSpan.FromSeconds(1),
            ReservationLease = TimeSpan.FromSeconds(3),
        };
        await using var firstDown = new TestWebhookListener();
        firstDown.SetDown(true);
        firstDown.Start();
        await using var secondDown = new TestWebhookListener();
        secondDown.SetDown(true);
        secondDown.Start();
        await using var healthy = new TestWebhookListener();
        healthy.Start();
        await CreateSubscriptionAsync(firstDown.Uri, "first-down-secret", ["test.event"]);
        await CreateSubscriptionAsync(secondDown.Uri, "second-down-secret", ["test.event"]);
        await CreateSubscriptionAsync(healthy.Uri, "healthy-last-secret", ["test.event"]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var id = await InsertEventAsync(occurredAt: clock.GetUtcNow().AddSeconds(-1));
        var dispatcher = fixture.CreateDispatchService(options, clock);

        await dispatcher.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));
        await dispatcher.RunOnceAsync(CancellationToken.None);

        Assert.Equal([id], healthy.AcceptedRequests.Select(r => r.EventId));
    }

    [Fact]
    public async Task ReserveDueAsync_PutsEventsWithoutAnAttemptAndThenTheLongestUnattemptedFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var attemptedRecently = await InsertEventAsync(occurredAt: now.AddMinutes(-30), lastAttemptAt: now.AddSeconds(-10));
        var attemptedLongAgo = await InsertEventAsync(occurredAt: now.AddMinutes(-10), lastAttemptAt: now.AddMinutes(-5));
        var neverAttempted = await InsertEventAsync(occurredAt: now.AddMinutes(-1));

        var reserved = await fixture.CreateDispatchService(timeProvider: new FakeTimeProvider(now)).ReserveDueAsync(CancellationToken.None);

        Assert.Equal([neverAttempted, attemptedLongAgo, attemptedRecently], reserved.Select(e => e.Id));
    }

    [Fact]
    public async Task RunOnceAsync_WildcardSubscriptionReceivesEveryEventType()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();

        await CreateSubscriptionAsync(listener.Uri, "wildcard-secret", ["*"]);

        var (postId, slug) = await CreatePublishedPictureAsync();
        await PostCommentAsync(slug);

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        Assert.Single(listener.Requests);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.SingleAsync(e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
        Assert.NotNull(outboxEvent.DispatchedAt);
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

    /// <summary>
    /// Inserts an event whose payload is valid jsonb nested deeper than the envelope builder
    /// accepts, so building its delivery body fails.
    /// </summary>
    private async Task<Guid> InsertCorruptEventAsync(DateTimeOffset occurredAt)
    {
        var id = Guid.CreateVersion7();
        var payload = new string('[', 100) + new string(']', 100);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO outbox_event (id, occurred_at, event_type, aggregate_type, aggregate_id, payload, next_attempt_at)
            VALUES ({id}, {occurredAt}, 'test.event', 'post', '1', {payload}::jsonb, {occurredAt})
            """);
        return id;
    }

    private async Task<Guid> InsertEventAsync(
        DateTimeOffset occurredAt,
        DateTimeOffset? nextAttemptAt = null,
        string eventType = "test.event",
        DateTimeOffset? lastAttemptAt = null)
    {
        await using var db = fixture.CreateDbContext();
        var outboxEvent = OutboxEvent.Create(eventType, "post", "1", """{"postId":1}""", occurredAt);
        outboxEvent.NextAttemptAt = nextAttemptAt ?? occurredAt;
        outboxEvent.LastAttemptAt = lastAttemptAt;
        outboxEvent.AttemptCount = lastAttemptAt is null ? 0 : 1;
        db.OutboxEvents.Add(outboxEvent);
        await db.SaveChangesAsync();
        return outboxEvent.Id;
    }

    private async Task<(long PostId, string Slug)> CreatePublishedPictureAsync()
    {
        await using var db = fixture.CreateDbContext();

        var author = new Profile
        {
            Username = $"author-{Guid.NewGuid():N}"[..24],
            DisplayName = "Outbox Test Author",
            Kind = ProfileKind.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Profiles.Add(author);
        await db.SaveChangesAsync();

        var post = new Post
        {
            AuthorProfileId = author.Id,
            Kind = PostKind.Single,
            Topic = PostTopic.Cichlids,
            Title = "Outbox Dispatch Target",
            State = PostState.Published,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        var slug = $"outbox-target-{Guid.NewGuid():N}"[..28];
        db.SlugAliases.Add(new SlugAlias
        {
            PostId = post.Id, Value = slug, IsCanonical = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return (post.Id, slug);
    }

    private async Task<long> PostCommentAsync(string slug)
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), $"writer-{Guid.NewGuid():N}"[..24]);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/pictures/{slug}/comments")
        {
            Content = JsonContent.Create(new CreateCommentRequest("Nice fish!", null)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonSerializer.Deserialize<CommentCreatedDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
        return created.Comment!.Id;
    }
}
