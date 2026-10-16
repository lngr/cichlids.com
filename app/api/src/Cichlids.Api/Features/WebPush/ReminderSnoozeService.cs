using System.Globalization;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.WebPush;

/// <summary>
/// Moves the reminder of one care rule of a tank, sent for its current due date, to 09:00 on the
/// next local day of the member's browser. A rule holds one snooze per due date.
/// </summary>
public sealed class ReminderSnoozeService(CichlidsDbContext context, CarePlanService plans, TimeProvider clock)
{
    /// <summary>
    /// Stores the snooze of the rule for the member and returns the local date on which the
    /// reminder returns. The due date must be the current due date of the rule.
    /// </summary>
    public async Task<(DateOnly? UntilDate, string? Error)> SnoozeAsync(long profileId, SnoozeReminderRequest request, CancellationToken ct)
    {
        if (!SnakeCaseEnum.TryParse<Domain.Enums.CareKind>(request.Kind, out var kind)) return (null, "care_kind_invalid");
        if (!DateOnly.TryParseExact(request.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate)
            || !TimeZones.TryFind(request.TimeZone, out var zone))
        {
            return (null, "web_push_snooze_invalid");
        }

        var now = clock.GetUtcNow();
        var local = LocalMoment.Of(now, zone);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, request.TankId, profileId, ct)) return (null, "care_not_found");
        var plan = await plans.GetPlanAsync(request.TankId, local.Date, local.UtcOffsetMinutes, ct);
        var current = plan.Rules.FirstOrDefault(r => r.Kind == kind)?.Due?.DueDate;
        if (current != dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) return (null, "web_push_snooze_invalid");

        var until = local.Date.AddDays(1);
        var snooze = await context.CareReminderSnoozes.SingleOrDefaultAsync(
            s => s.ProfileId == profileId && s.TankId == request.TankId && s.Kind == kind && s.DueDate == dueDate, ct);
        if (snooze is null)
        {
            snooze = new CareReminderSnooze { ProfileId = profileId, TankId = request.TankId, Kind = kind, DueDate = dueDate, CreatedAt = now };
            context.CareReminderSnoozes.Add(snooze);
        }

        snooze.UntilDate = until;
        await context.SaveChangesAsync(ct);
        context.OutboxEvents.Add(OutboxEvent.Create("care_reminder.snoozed", "care_reminder", snooze.Id.ToString(),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["snoozeId"] = snooze.Id, ["profileId"] = profileId, ["tankId"] = request.TankId, ["kind"] = SnakeCaseEnum.ToSnakeCase(kind),
                ["dueDate"] = request.DueDate, ["untilDate"] = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["snoozedAt"] = now,
            }), now));
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (until, null);
    }
}
