using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// Stores and removes the Web Push subscriptions of members. A subscription is accepted only while
/// Web Push is available, for an https endpoint of an allowlisted push service, with valid keys and
/// a known time zone, and a member keeps a limited number of them. Every change records an outbox
/// event in its transaction.
/// </summary>
public sealed partial class WebPushSubscriptionService(CichlidsDbContext context, WebPushSettings settings, TimeProvider clock)
{
    private const int MaxEndpointLength = 2048;
    private const int PublicKeyLength = 65;
    private const int AuthSecretLength = 16;

    [GeneratedRegex("^[A-Za-z0-9_+\\-]+(/[A-Za-z0-9_+\\-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneId();

    /// <summary>
    /// Stores the subscription of a browser for the member, or updates the keys and time zone of the
    /// subscription the member already has for the endpoint (Created is false then). A subscription
    /// of another member for the same endpoint moves to the caller, since the endpoint names the
    /// browser the caller is using.
    /// </summary>
    public async Task<(WebPushSubscriptionDto? Subscription, bool Created, string? Error)> SubscribeAsync(
        long profileId, SubscribeWebPushRequest request, CancellationToken ct)
    {
        if (!settings.Available) return (null, false, "web_push_unavailable");
        if (Validate(request) is { } error) return (null, false, error);
        var endpoint = request.Endpoint!;
        var keys = request.Keys!;
        var now = clock.GetUtcNow();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.profile WHERE id = {profileId} FOR UPDATE", ct);
        // Members subscribing the same browser at once take turns, so the unique endpoint is never
        // inserted twice; the lock ends with the transaction.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({endpoint}, 0))", ct);
        var existing = await context.WebPushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is not null && existing.ProfileId == profileId)
        {
            existing.P256dh = keys.P256dh!;
            existing.Auth = keys.Auth!;
            existing.TimeZone = request.TimeZone!;
            existing.FailureCount = 0;
            existing.NextAttemptAt = null;
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (ToDto(existing), false, null);
        }

        if (existing is not null)
        {
            context.WebPushSubscriptions.Remove(existing);
            context.OutboxEvents.Add(UnsubscribedEvent(existing, now));
        }

        if (await context.WebPushSubscriptions.CountAsync(s => s.ProfileId == profileId, ct) >= settings.MaxSubscriptionsPerMember)
        {
            return (null, false, "web_push_limit_reached");
        }

        var created = new WebPushSubscription
        {
            ProfileId = profileId, Endpoint = endpoint, P256dh = keys.P256dh!, Auth = keys.Auth!, TimeZone = request.TimeZone!, CreatedAt = now,
        };
        context.WebPushSubscriptions.Add(created);
        await context.SaveChangesAsync(ct);
        context.OutboxEvents.Add(OutboxEvent.Create("web_push.subscribed", "web_push_subscription", created.Id.ToString(),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["subscriptionId"] = created.Id, ["profileId"] = profileId, ["timeZone"] = created.TimeZone, ["createdAt"] = now,
            }), now));
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ToDto(created), true, null);
    }

    /// <summary>
    /// Deletes the member's subscription for the endpoint. An endpoint the member has no
    /// subscription for changes nothing.
    /// </summary>
    public async Task UnsubscribeAsync(long profileId, string? endpoint, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > MaxEndpointLength) return;
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var existing = await context.WebPushSubscriptions.SingleOrDefaultAsync(s => s.ProfileId == profileId && s.Endpoint == endpoint, ct);
        if (existing is not null)
        {
            context.WebPushSubscriptions.Remove(existing);
            context.OutboxEvents.Add(UnsubscribedEvent(existing, clock.GetUtcNow()));
            await context.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private string? Validate(SubscribeWebPushRequest request)
    {
        if (string.IsNullOrEmpty(request.Endpoint) || request.Endpoint.Length > MaxEndpointLength
            || !Uri.TryCreate(request.Endpoint, UriKind.Absolute, out _))
        {
            return "web_push_subscription_invalid";
        }

        if (!settings.IsAllowedEndpoint(request.Endpoint)) return "web_push_endpoint_not_allowed";
        if (request.Keys is not { P256dh: { } p256dh, Auth: { } auth } || !IsPublicKey(p256dh) || !IsAuthSecret(auth)) return "web_push_subscription_invalid";
        return IsTimeZone(request.TimeZone) ? null : "web_push_subscription_invalid";
    }

    private static bool IsPublicKey(string value)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value);
            if (bytes.Length != PublicKeyLength || bytes[0] != 0x04) return false;
            using var key = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = bytes[1..33], Y = bytes[33..65] },
            });
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static bool IsAuthSecret(string value)
    {
        try
        {
            return WebEncoders.Base64UrlDecode(value).Length == AuthSecretLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsTimeZone(string? value) =>
        value is { Length: > 0 and <= 64 } && TimeZoneId().IsMatch(value) && TimeZoneInfo.TryFindSystemTimeZoneById(value, out _);

    private static OutboxEvent UnsubscribedEvent(WebPushSubscription subscription, DateTimeOffset now) =>
        OutboxEvent.Create("web_push.unsubscribed", "web_push_subscription", subscription.Id.ToString(),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["subscriptionId"] = subscription.Id, ["profileId"] = subscription.ProfileId, ["unsubscribedAt"] = now,
            }), now);

    private static WebPushSubscriptionDto ToDto(WebPushSubscription subscription) =>
        new(subscription.Id, subscription.TimeZone, subscription.CreatedAt);
}
