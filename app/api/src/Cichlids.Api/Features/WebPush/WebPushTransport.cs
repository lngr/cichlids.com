using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Cichlids.Domain.Entities;

namespace Cichlids.Api.Features.WebPush;

/// <summary>How a push service answered a message.</summary>
public enum PushOutcome
{
    /// <summary>The push service accepted the message.</summary>
    Accepted,

    /// <summary>The push service does not know the subscription (404 or 410).</summary>
    Gone,

    /// <summary>The message was not delivered for another reason; trying again later may work.</summary>
    Failed,
}

/// <summary>The outcome of a send and, for a failure, how long the push service asked to wait.</summary>
public sealed record PushResult(PushOutcome Outcome, TimeSpan? RetryAfter = null);

/// <summary>Sends one encrypted message to the push service of a subscription.</summary>
public interface IWebPushTransport
{
    /// <summary>Encrypts the payload for the subscription and delivers it to its push service.</summary>
    Task<PushResult> SendAsync(WebPushSubscription subscription, string payload, CancellationToken ct);
}

/// <summary>
/// Sends Web Push messages with the library Lib.Net.Http.WebPush: the payload is encrypted for the
/// browser with the content coding aes128gcm (RFC 8291) and the request is authenticated with a
/// VAPID token (RFC 8292) signed by the private key of the server.
/// </summary>
public sealed class WebPushTransport : IWebPushTransport, IDisposable
{
    /// <summary>The name of the HTTP client that reaches the push services.</summary>
    public const string HttpClientName = "web-push";

    private const int TimeToLiveSeconds = 8 * 60 * 60;
    private readonly HttpClient _http;
    private readonly VapidAuthentication _vapid;

    public WebPushTransport(HttpClient http, WebPushSettings settings)
    {
        _http = http;
        _vapid = new VapidAuthentication(settings.PublicKey!, settings.PrivateKey!) { Subject = settings.Subject };
    }

    public async Task<PushResult> SendAsync(WebPushSubscription subscription, string payload, CancellationToken ct)
    {
        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        var message = new PushMessage(payload) { TimeToLive = TimeToLiveSeconds, Urgency = PushMessageUrgency.Normal };
        var client = new PushServiceClient(_http) { AutoRetryAfter = false };
        try
        {
            await client.RequestPushMessageDeliveryAsync(target, message, _vapid, VapidAuthenticationScheme.Vapid, ct);
            return new PushResult(PushOutcome.Accepted);
        }
        catch (PushServiceClientException exception)
        {
            return exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone
                ? new PushResult(PushOutcome.Gone)
                : new PushResult(PushOutcome.Failed, exception.Headers?.RetryAfter?.Delta);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new PushResult(PushOutcome.Failed);
        }
    }

    public void Dispose() => _vapid.Dispose();
}

/// <summary>The transport of a server without a VAPID key pair; sending is never attempted.</summary>
public sealed class UnavailableWebPushTransport : IWebPushTransport
{
    public Task<PushResult> SendAsync(WebPushSubscription subscription, string payload, CancellationToken ct) =>
        throw new InvalidOperationException("Web Push is unavailable: there is no VAPID key pair.");
}
