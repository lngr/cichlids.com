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
    TimeProvider timeProvider,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxDispatchService> logger)
{
    public const string HttpClientName = "outbox-webhook-delivery";
    private const string SignatureHeaderName = "X-Cichlids-Signature";

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
                .ToList();

            var remainingLease = outboxEvent.NextAttemptAt - Now();
            if (remainingLease < settings.DeliveryTimeout * Math.Max(1, subscribers.Count))
            {
                logger.LogInformation(
                    "Outbox pass stops before event {EventId}: the remaining reservation of {RemainingLease} is too short",
                    outboxEvent.Id, remainingLease);
                return;
            }

            var outcome = await DeliverAsync(outboxEvent, subscribers, cancellationToken);
            await RecordAsync(outboxEvent, outcome, settings, cancellationToken);
        }
    }

    private async Task<DeliveryOutcome> DeliverAsync(
        OutboxEvent outboxEvent, IReadOnlyList<WebhookSubscription> subscribers, CancellationToken cancellationToken)
    {
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

                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return DeliveryOutcome.Failed($"{subscriber.Url} responded {(int)response.StatusCode}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return DeliveryOutcome.Failed($"{subscriber.Url}: {ex.Message}");
            }
        }

        return DeliveryOutcome.Succeeded;
    }

    private async Task RecordAsync(
        OutboxEvent reserved, DeliveryOutcome outcome, OutboxDispatcherOptions settings, CancellationToken cancellationToken)
    {
        var now = Now();
        var reservedUntil = reserved.NextAttemptAt;
        var stillReserved = context.OutboxEvents.Where(e =>
            e.Id == reserved.Id && e.NextAttemptAt == reservedUntil && e.DispatchedAt == null && e.GivenUpAt == null);

        int updated;
        if (outcome.Error is null)
        {
            updated = await stillReserved.ExecuteUpdateAsync(s => s.SetProperty(e => e.DispatchedAt, now), cancellationToken);
        }
        else
        {
            var attemptCount = reserved.AttemptCount + 1;
            var (nextAttemptAt, givenUp) = OutboxRetrySchedule.AfterFailure(
                attemptCount, now, reserved.RedeliveredAt ?? reserved.OccurredAt, settings);
            DateTimeOffset? givenUpAt = givenUp ? now : null;

            updated = await stillReserved.ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.AttemptCount, attemptCount)
                    .SetProperty(e => e.LastError, outcome.Error)
                    .SetProperty(e => e.LastAttemptAt, now)
                    .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                    .SetProperty(e => e.GivenUpAt, givenUpAt),
                cancellationToken);

            if (updated == 1 && givenUp)
            {
                logger.LogWarning(
                    "Outbox event {EventId} ({EventType}) is given up after {AttemptCount} attempts. Last error: {Error}",
                    reserved.Id, reserved.EventType, attemptCount, outcome.Error);
            }
        }

        if (updated == 0)
        {
            logger.LogWarning(
                "The delivery result of outbox event {EventId} is discarded because its reservation ended",
                reserved.Id);
        }
    }

    /// <summary>
    /// The current time at the microsecond precision Postgres stores, so a reservation value read
    /// back from the database equals the value this instance computed.
    /// </summary>
    private DateTimeOffset Now()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
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

    private sealed record DeliveryOutcome(string? Error)
    {
        public static readonly DeliveryOutcome Succeeded = new((string?)null);

        public static DeliveryOutcome Failed(string error) => new(error);
    }
}
