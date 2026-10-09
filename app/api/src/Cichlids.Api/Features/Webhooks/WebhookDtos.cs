namespace Cichlids.Api.Features.Webhooks;

public sealed record CreateWebhookRequest(string? Url, string? Secret, IReadOnlyList<string>? EventTypes);

/// <summary>
/// A registered webhook subscription as returned to an admin caller. The signing secret is
/// write-only: it is accepted on creation but never echoed back here.
/// </summary>
public sealed record WebhookDto(long Id, string Url, IReadOnlyList<string> EventTypes, bool Active, DateTimeOffset CreatedAt);
