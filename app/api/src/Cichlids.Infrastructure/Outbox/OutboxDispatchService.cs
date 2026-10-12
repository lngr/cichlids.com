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
/// Delivers due outbox events to their matching webhook subscribers. A dispatch pass reserves a
/// batch of due events in one short statement by moving their next attempt time to the end of a
/// lease, delivers them without any transaction or row lock, and records each result in its own
/// update. That update only applies while the row still holds the lease value this instance set,
/// so a result that arrives after another instance took over the event is discarded. Events of an
/// instance that stops mid-pass become due again once the lease ends.
/// </summary>
public sealed class OutboxDispatchService(
    CichlidsDbContext context,
    IHttpClientFactory httpClientFactory,
    WebhookTargetPolicy targetPolicy,
    TimeProvider timeProvider,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxDispatchService> logger)
{
    public const string HttpClientName = "outbox-webhook-delivery";
    private const string SignatureHeaderName = "X-Cichlids-Signature";
    private const int MaxErrorLength = 1000;

    /// <summary>
    /// Reserves and processes one batch of due events. Returns the number of events reserved, so a
    /// caller can run the next pass at once when a full batch was reserved.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var reserved = await ReserveDueAsync(cancellationToken);
        if (reserved.Count > 0)
        {
            await ProcessAsync(reserved, cancellationToken);
        }

        return reserved.Count;
    }

    /// <summary>
    /// Reserves up to one batch of open events whose next attempt time has come, oldest due first,
    /// and returns them with the end of the reservation as their next attempt time. Rows another
    /// instance is reserving at the same moment are skipped.
    /// </summary>
    public async Task<IReadOnlyList<OutboxEvent>> ReserveDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = Now();
        var reservedUntil = now + settings.ReservationLease;

        var reserved = await context.OutboxEvents.FromSqlInterpolated(
            $"""
            UPDATE outbox_event AS e
            SET next_attempt_at = {reservedUntil}
            FROM (
                SELECT id FROM outbox_event
                WHERE dispatched_at IS NULL AND given_up_at IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
            ) AS due
            WHERE e.id = due.id
            RETURNING e.*
            """).AsNoTracking().ToListAsync(cancellationToken);

        return reserved.OrderBy(e => e.OccurredAt).ToList();
    }

    /// <summary>
    /// Delivers and records the given reserved events one after another. The pass stops before an
    /// event whose remaining lease is too short for its deliveries; that event and the rest become
    /// due again when their lease ends.
    /// </summary>
    public async Task ProcessAsync(IReadOnlyList<OutboxEvent> reserved, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var activeSubscriptions = await context.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => s.Active)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var outboxEvent in reserved)
        {
            var subscribers = activeSubscriptions
                .Where(s => s.EventTypes.Contains(outboxEvent.EventType) || s.EventTypes.Contains("*"))
                .Where(s => !outboxEvent.AcknowledgedSubscriptionIds.Contains(s.Id))
                .ToList();

            var remainingLease = outboxEvent.NextAttemptAt - Now();
            if (remainingLease < settings.DeliveryTimeout * Math.Max(1, subscribers.Count))
            {
                logger.LogInformation(
                    "Outbox pass stops before event {EventId}: the remaining reservation of {RemainingLease} is too short",
                    outboxEvent.Id, remainingLease);
                return;
            }

            DeliveryOutcome outcome;
            try
            {
                outcome = await DeliverAsync(outboxEvent, subscribers, settings, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Delivering outbox event {EventId} failed", outboxEvent.Id);
                outcome = DeliveryOutcome.Failed($"delivery failed: {ex.GetType().Name}");
            }

            try
            {
                await RecordAsync(outboxEvent, outcome, settings, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    ex, "Recording the delivery result of outbox event {EventId} failed; it is due again when its reservation ends",
                    outboxEvent.Id);
            }
        }
    }

    private async Task<DeliveryOutcome> DeliverAsync(
        OutboxEvent outboxEvent,
        IReadOnlyList<WebhookSubscription> subscribers,
        OutboxDispatcherOptions settings,
        CancellationToken cancellationToken)
    {
        var body = BuildDeliveryBody(outboxEvent);
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var acknowledged = new List<long>();
        var errors = new List<string>();

        foreach (var subscriber in subscribers)
        {
            var error = await DeliverToAsync(httpClient, subscriber, body, settings, cancellationToken);
            if (error is null)
            {
                acknowledged.Add(subscriber.Id);
            }
            else
            {
                errors.Add(error);
            }
        }

        return new DeliveryOutcome(acknowledged, errors);
    }

    /// <summary>
    /// Posts the body to one subscriber and returns null on a success status, otherwise an error
    /// naming the subscription and the status or exception type. A subscriber whose target the
    /// allowlist rejects is never contacted.
    /// </summary>
    private async Task<string?> DeliverToAsync(
        HttpClient httpClient,
        WebhookSubscription subscriber,
        string body,
        OutboxDispatcherOptions settings,
        CancellationToken cancellationToken)
    {
        if (!targetPolicy.IsAllowed(subscriber.Url, out _))
        {
            return $"subscription {subscriber.Id} target is not allowed";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.DeliveryTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscriber.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(SignatureHeaderName, Sign(body, subscriber.Secret));

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode
                ? null
                : $"subscription {subscriber.Id} responded {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"subscription {subscriber.Id} timed out";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Delivering to webhook subscription {SubscriptionId} failed", subscriber.Id);
            return $"subscription {subscriber.Id} failed: {ex.GetType().Name}";
        }
    }

    private async Task RecordAsync(
        OutboxEvent reserved, DeliveryOutcome outcome, OutboxDispatcherOptions settings, CancellationToken cancellationToken)
    {
        var now = Now();
        var reservedUntil = reserved.NextAttemptAt;
        var stillReserved = context.OutboxEvents.Where(e =>
            e.Id == reserved.Id && e.NextAttemptAt == reservedUntil && e.DispatchedAt == null && e.GivenUpAt == null);

        var acknowledged = reserved.AcknowledgedSubscriptionIds.Union(outcome.Acknowledged).ToList();

        int updated;
        if (outcome.Errors.Count == 0)
        {
            updated = await stillReserved.ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.DispatchedAt, now)
                    .SetProperty(e => e.AcknowledgedSubscriptionIds, acknowledged),
                cancellationToken);
        }
        else
        {
            var error = string.Join("; ", outcome.Errors);
            if (error.Length > MaxErrorLength)
            {
                error = error[..MaxErrorLength];
            }

            var attemptCount = reserved.AttemptCount + 1;
            var (nextAttemptAt, givenUp) = OutboxRetrySchedule.AfterFailure(
                attemptCount, now, reserved.RedeliveredAt ?? reserved.OccurredAt, settings);
            DateTimeOffset? givenUpAt = givenUp ? now : null;

            updated = await stillReserved.ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.AttemptCount, attemptCount)
                    .SetProperty(e => e.LastError, error)
                    .SetProperty(e => e.LastAttemptAt, now)
                    .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                    .SetProperty(e => e.GivenUpAt, givenUpAt)
                    .SetProperty(e => e.AcknowledgedSubscriptionIds, acknowledged),
                cancellationToken);

            if (updated == 1 && givenUp)
            {
                logger.LogWarning(
                    "Outbox event {EventId} ({EventType}) is given up after {AttemptCount} attempts. Last error: {Error}",
                    reserved.Id, reserved.EventType, attemptCount, error);
            }
        }

        if (updated == 0)
        {
            logger.LogWarning(
                "The delivery result of outbox event {EventId} is discarded because its reservation ended",
                reserved.Id);
        }
    }

    private DateTimeOffset Now() => OutboxTime.UtcNow(timeProvider);

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

    /// <summary>
    /// The subscriptions that acknowledged one delivery attempt of an event and the errors of
    /// those that did not. An attempt without errors means every matching subscriber has
    /// acknowledged the event.
    /// </summary>
    private sealed record DeliveryOutcome(IReadOnlyList<long> Acknowledged, IReadOnlyList<string> Errors)
    {
        public static DeliveryOutcome Failed(string error) => new([], [error]);
    }
}
