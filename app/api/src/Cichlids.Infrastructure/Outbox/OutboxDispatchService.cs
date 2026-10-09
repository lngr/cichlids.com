using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Outbox;

/// <summary>
/// Delivers due outbox events to their matching webhook subscribers. A dispatch pass locks a
/// batch of undispatched rows with SKIP LOCKED so concurrent dispatcher instances split the
/// backlog instead of racing on the same rows, then signs and posts each event to every active
/// subscription whose event_types entry matches (including the "*" wildcard). An event is marked
/// dispatched once every matching subscriber accepted it with a 2xx response, or immediately when
/// it has no matching subscribers; a subscriber failure leaves the event for redelivery after a
/// backoff computed from its attempt count, until the configured attempt ceiling is reached and
/// the event is dropped as a dead letter.
/// </summary>
public sealed class OutboxDispatchService(
    CichlidsDbContext context,
    IHttpClientFactory httpClientFactory,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxDispatchService> logger)
{
    public const string HttpClientName = "outbox-webhook-delivery";
    private const string SignatureHeaderName = "X-Cichlids-Signature";

    /// <summary>
    /// Locks and considers one batch of undispatched events in a single transaction. Returns the
    /// number of rows the batch selected, including rows left untouched this pass because their
    /// backoff has not elapsed yet, so callers such as tests can tell an empty backlog apart from
    /// "nothing was due yet".
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var batch = await context.OutboxEvents.FromSqlInterpolated(
            $"""
            SELECT * FROM outbox_event
            WHERE dispatched_at IS NULL
            ORDER BY occurred_at
            LIMIT {settings.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        if (batch.Count > 0)
        {
            var activeSubscriptions = await context.WebhookSubscriptions
                .Where(s => s.Active)
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;

            foreach (var outboxEvent in batch)
            {
                var due = outboxEvent.OccurredAt + OutboxBackoff.Compute(outboxEvent.AttemptCount, settings.MaxBackoff);
                if (now < due)
                {
                    continue;
                }

                await DeliverAsync(outboxEvent, activeSubscriptions, settings, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task DeliverAsync(
        OutboxEvent outboxEvent,
        IReadOnlyList<WebhookSubscription> activeSubscriptions,
        OutboxDispatcherOptions settings,
        CancellationToken cancellationToken)
    {
        var subscribers = activeSubscriptions
            .Where(s => s.EventTypes.Contains(outboxEvent.EventType) || s.EventTypes.Contains("*"))
            .ToList();

        var body = BuildDeliveryBody(outboxEvent);
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        foreach (var subscriber in subscribers)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscriber.Url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add(SignatureHeaderName, Sign(body, subscriber.Secret));

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Fail(outboxEvent, settings, $"{subscriber.Url} responded {(int)response.StatusCode}");
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Fail(outboxEvent, settings, $"{subscriber.Url}: {ex.Message}");
                return;
            }
        }

        outboxEvent.DispatchedAt = DateTimeOffset.UtcNow;
    }

    private void Fail(OutboxEvent outboxEvent, OutboxDispatcherOptions settings, string error)
    {
        outboxEvent.AttemptCount++;
        outboxEvent.LastError = error;

        if (outboxEvent.AttemptCount >= settings.MaxAttempts)
        {
            outboxEvent.DispatchedAt = DateTimeOffset.UtcNow;
            outboxEvent.LastError = "gave up";
            logger.LogWarning(
                "Outbox event {EventId} ({EventType}) gave up after {AttemptCount} attempts. Last error: {Error}",
                outboxEvent.Id, outboxEvent.EventType, outboxEvent.AttemptCount, error);
        }
    }

    private static string Sign(string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    /// <summary>
    /// Builds the delivery envelope a subscriber receives. The envelope's "id" is the outbox
    /// event's own id, stable across redeliveries, so a subscriber that records ids it has
    /// already processed gets exactly-once effects even though delivery itself is at-least-once.
    /// </summary>
    private static string BuildDeliveryBody(OutboxEvent outboxEvent)
    {
        var envelope = new JsonObject
        {
            ["id"] = outboxEvent.Id,
            ["eventType"] = outboxEvent.EventType,
            ["aggregateType"] = outboxEvent.AggregateType,
            ["aggregateId"] = outboxEvent.AggregateId,
            ["occurredAt"] = outboxEvent.OccurredAt,
            ["payload"] = JsonNode.Parse(outboxEvent.Payload),
        };
        return envelope.ToJsonString();
    }
}
