namespace Cichlids.Domain.Entities;

/// <summary>
/// The Web Push subscription of one browser of a member: the address of the push service for the
/// browser, the keys with which messages for it are encrypted, and the time zone in which the
/// browser's reminders fall due. The endpoint identifies the browser and is unique.
/// </summary>
public class WebPushSubscription
{
    public long Id { get; set; }
    public long ProfileId { get; set; }

    /// <summary>The https address of the push service for this browser.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The public key of the browser (P-256, uncompressed point) as base64url.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>The authentication secret of the subscription (16 bytes) as base64url.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>The IANA time zone of the browser.</summary>
    public string TimeZone { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the push service last accepted a message for this subscription; null before the first one.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Failed sends since the last accepted message.</summary>
    public int FailureCount { get; set; }

    /// <summary>The earliest time the next send is attempted after a failure; null without one.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
