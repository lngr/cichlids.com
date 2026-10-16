namespace Cichlids.Api.Features.WebPush;

/// <summary>The keys of a browser's push subscription as base64url, in the shape of PushSubscription.toJSON.</summary>
public sealed record WebPushKeysRequest(string? P256dh, string? Auth);

/// <summary>A browser's push subscription as the web app posts it, with the IANA time zone of the browser.</summary>
public sealed record SubscribeWebPushRequest(string? Endpoint, WebPushKeysRequest? Keys, string? TimeZone);

/// <summary>The subscription to delete, named by its endpoint.</summary>
public sealed record UnsubscribeWebPushRequest(string? Endpoint);

/// <summary>Whether Web Push is available on this server and the VAPID public key a browser subscribes with.</summary>
public sealed record WebPushConfigDto(bool Available, string? PublicKey);

/// <summary>A stored subscription of the caller.</summary>
public sealed record WebPushSubscriptionDto(long Id, string TimeZone, DateTimeOffset CreatedAt);
