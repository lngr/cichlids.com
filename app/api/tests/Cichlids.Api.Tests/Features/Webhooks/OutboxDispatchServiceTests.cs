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

    private async Task<Guid> InsertEventAsync(
        DateTimeOffset occurredAt, DateTimeOffset? nextAttemptAt = null, string eventType = "test.event")
    {
        await using var db = fixture.CreateDbContext();
        var outboxEvent = OutboxEvent.Create(eventType, "post", "1", """{"postId":1}""", occurredAt);
        outboxEvent.NextAttemptAt = nextAttemptAt ?? occurredAt;
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
