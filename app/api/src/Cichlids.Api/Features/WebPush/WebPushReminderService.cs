using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.WebPush;

/// <summary>The counts of one sender pass.</summary>
public sealed record WebPushPass(int Sent, int Removed, int Failed);

/// <summary>
/// One pass of the Web Push reminder sender: for each subscription whose own clock has reached
/// 09:00 it sends the reminders that fall on the local day, once per rule, due date, day and
/// subscription. A record in web_push_reminder_sent is created before the send and its unique key
/// lets exactly one instance send a reminder. A subscription the push service does not know is
/// deleted; other failures release the record and delay the next attempt of the subscription
/// exponentially, from five minutes to six hours, or by the Retry-After of the push service.
/// </summary>
public sealed class WebPushReminderService(
    CichlidsDbContext context,
    CarePlanService plans,
    IWebPushTransport transport,
    WebPushSettings settings,
    TimeProvider clock,
    ILogger<WebPushReminderService> logger)
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan RecordRetention = TimeSpan.FromDays(3);

    public async Task<WebPushPass> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var subscriptions = await context.WebPushSubscriptions
            .Where(s => s.NextAttemptAt == null || s.NextAttemptAt <= now)
            .OrderBy(s => s.ProfileId).ThenBy(s => s.Id).ToListAsync(ct);
        var due = new Dictionary<(long Profile, int Offset, DateOnly Date), IReadOnlyList<DueCareItemDto>>();
        var snoozes = new Dictionary<(long Profile, DateOnly Date), IReadOnlyList<ReminderSnoozeRecord>>();
        int sent = 0, removed = 0, failed = 0;

        foreach (var subscription in subscriptions)
        {
            if (!settings.IsAllowedEndpoint(subscription.Endpoint))
            {
                await RemoveAsync(subscription, "endpoint_not_allowed", now, ct);
                removed++;
                continue;
            }

            if (!TimeZones.TryFind(subscription.TimeZone, out var zone)) continue;
            var local = LocalMoment.Of(now, zone);
            if (local.TimeOfDay < TimeSpan.FromHours(WebReminderSelection.ReminderHour)) continue;

            var dueKey = (subscription.ProfileId, local.UtcOffsetMinutes, local.Date);
            if (!due.TryGetValue(dueKey, out var items))
            {
                items = (await plans.ListDueAsync(subscription.ProfileId, local.Date, local.UtcOffsetMinutes, ct)).Items;
                due[dueKey] = items;
            }

            var snoozeKey = (subscription.ProfileId, local.Date);
            if (!snoozes.TryGetValue(snoozeKey, out var snoozed))
            {
                snoozed = (await context.CareReminderSnoozes.AsNoTracking()
                    .Where(s => s.ProfileId == subscription.ProfileId && s.UntilDate == local.Date).ToListAsync(ct))
                    .Select(s => new ReminderSnoozeRecord(s.TankId, s.Kind, s.DueDate, s.UntilDate)).ToList();
                snoozes[snoozeKey] = snoozed;
            }

            foreach (var reminder in WebReminderSelection.Select(items, snoozed, local))
            {
                if (!await ClaimAsync(subscription, reminder, now, ct)) continue;
                var result = await transport.SendAsync(subscription, Payload(reminder), ct);
                if (result.Outcome == PushOutcome.Accepted)
                {
                    sent++;
                    subscription.LastSuccessAt = now;
                    subscription.FailureCount = 0;
                    subscription.NextAttemptAt = null;
                    await context.SaveChangesAsync(ct);
                    continue;
                }

                if (result.Outcome == PushOutcome.Gone)
                {
                    await RemoveAsync(subscription, "gone", now, ct);
                    removed++;
                }
                else
                {
                    await ReleaseAsync(subscription, reminder, ct);
                    subscription.FailureCount++;
                    subscription.NextAttemptAt = now + Backoff(subscription.FailureCount, result.RetryAfter);
                    await context.SaveChangesAsync(ct);
                    failed++;
                    logger.LogWarning("Web Push to subscription {SubscriptionId} failed {Count} time(s) in a row; the next attempt is at {NextAttempt}.",
                        subscription.Id, subscription.FailureCount, subscription.NextAttemptAt);
                }

                break;
            }
        }

        await PruneAsync(now, ct);
        return new WebPushPass(sent, removed, failed);
    }

    private static TimeSpan Backoff(int failures, TimeSpan? retryAfter)
    {
        var exponential = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, FirstBackoff.Ticks * (1L << Math.Min(failures - 1, 16))));
        var requested = retryAfter is { } wait && wait > TimeSpan.Zero ? (wait < MaxRetryAfter ? wait : MaxRetryAfter) : TimeSpan.Zero;
        return requested > exponential ? requested : exponential;
    }

    private static string Payload(WebReminder reminder)
    {
        var payload = new Dictionary<string, object?>
        {
            ["tankId"] = reminder.TankId,
            ["tankTitle"] = reminder.TankTitle,
            ["kind"] = SnakeCaseEnum.ToSnakeCase(reminder.Kind),
            ["dueDate"] = reminder.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["url"] = $"/care?tank={reminder.TankId}",
        };
        if (reminder.DaysLeft is { } daysLeft) payload["daysLeft"] = daysLeft;
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Creates the record of a reminder for a subscription; false when another pass already holds it.</summary>
    private async Task<bool> ClaimAsync(WebPushSubscription subscription, WebReminder reminder, DateTimeOffset now, CancellationToken ct)
    {
        var kind = SnakeCaseEnum.ToSnakeCase(reminder.Kind);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.web_push_reminder_sent (subscription_id, tank_id, kind, due_date, fire_date, sent_at)
            VALUES ({subscription.Id}, {reminder.TankId}, {kind}, {reminder.DueDate}, {reminder.FireDate}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        return inserted == 1;
    }

    private Task<int> ReleaseAsync(WebPushSubscription subscription, WebReminder reminder, CancellationToken ct) =>
        context.WebPushRemindersSent.Where(r => r.SubscriptionId == subscription.Id && r.TankId == reminder.TankId && r.Kind == reminder.Kind
            && r.DueDate == reminder.DueDate && r.FireDate == reminder.FireDate).ExecuteDeleteAsync(ct);

    private async Task RemoveAsync(WebPushSubscription subscription, string reason, DateTimeOffset now, CancellationToken ct)
    {
        context.WebPushSubscriptions.Remove(subscription);
        context.OutboxEvents.Add(WebPushSubscriptionService.UnsubscribedEvent(subscription, reason, now));
        await context.SaveChangesAsync(ct);
    }

    private async Task PruneAsync(DateTimeOffset now, CancellationToken ct)
    {
        var oldest = DateOnly.FromDateTime((now - RecordRetention).UtcDateTime);
        await context.WebPushRemindersSent.Where(r => r.FireDate < oldest).ExecuteDeleteAsync(ct);
        await context.CareReminderSnoozes.Where(s => s.UntilDate < oldest).ExecuteDeleteAsync(ct);
    }
}
