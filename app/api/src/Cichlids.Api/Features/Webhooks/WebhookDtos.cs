namespace Cichlids.Api.Features.Webhooks;

public sealed record CreateWebhookRequest(string? Url, string? Secret, IReadOnlyList<string>? EventTypes);

/// <summary>
/// A registered webhook subscription as returned to an admin caller. The signing secret is
/// write-only: it is accepted on creation but never echoed back here.
/// </summary>
public sealed record WebhookDto(long Id, string Url, IReadOnlyList<string> EventTypes, bool Active, DateTimeOffset CreatedAt);

/// <summary>
/// An outbox event as listed to an admin caller, with its delivery state. The payload is left out.
/// </summary>
public sealed record OutboxEventDto(
    Guid Id,
    string EventType,
    string AggregateType,
    string AggregateId,
    DateTimeOffset OccurredAt,
    int AttemptCount,
    string? LastError,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? GivenUpAt,
    DateTimeOffset? RedeliveredAt);

/// <summary>
/// The number of given-up outbox events scheduled for redelivery.
/// </summary>
public sealed record OutboxRedeliveryDto(int Count);
