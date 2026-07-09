using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cichlids.Api.Features.Comments;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// Every test drives dispatch passes against the fixture's single shared Postgres container, so
/// a subscription left registered after one test would still match and receive events from a
/// later one. <see cref="DisposeAsync"/> removes every subscription this instance registered
/// (xUnit creates a fresh instance, and so calls it, per test method) so tests stay isolated
/// despite the shared database.
/// </summary>
[Collection(OutboxDispatchCollection.Name)]
public class OutboxDispatchServiceTests(OutboxDispatchFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _subscriptionIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_subscriptionIds.Count == 0)
        {
            return;
        }

        await using var db = fixture.CreateDbContext();
        await db.WebhookSubscriptions.Where(w => _subscriptionIds.Contains(w.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RunOnceAsync_DeliversDueEventExactlyOnceWithAValidSignature()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();

        const string secret = "matching-subscriber-secret";
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

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            var afterFirstAttempt = await db.OutboxEvents.SingleAsync(
                e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
            Assert.Null(afterFirstAttempt.DispatchedAt);
            Assert.Equal(1, afterFirstAttempt.AttemptCount);
            Assert.Contains("responded 500", afterFirstAttempt.LastError);

            // Backdates the event instead of sleeping through its backoff window, so the retry is
            // exercised deterministically and fast.
            afterFirstAttempt.OccurredAt = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        await fixture.CreateDispatchService().RunOnceAsync(CancellationToken.None);

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
    public async Task RunOnceAsync_TwoConcurrentDispatchersDoNotDeliverTheSameEventTwice()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();

        await CreateSubscriptionAsync(listener.Uri, "concurrent-secret", ["comment.created"]);

        var (postId, slug) = await CreatePublishedPictureAsync();
        await PostCommentAsync(slug);

        var first = fixture.CreateDispatchService();
        var second = fixture.CreateDispatchService();

        await Task.WhenAll(
            first.RunOnceAsync(CancellationToken.None),
            second.RunOnceAsync(CancellationToken.None));

        Assert.Single(listener.Requests);

        await using var db = fixture.CreateDbContext();
        var outboxEvent = await db.OutboxEvents.SingleAsync(e => e.EventType == "comment.created" && e.AggregateId == postId.ToString());
        Assert.NotNull(outboxEvent.DispatchedAt);
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

    private async Task CreateSubscriptionAsync(Uri url, string secret, IReadOnlyList<string> eventTypes)
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
        _subscriptionIds.Add(subscription.Id);
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
