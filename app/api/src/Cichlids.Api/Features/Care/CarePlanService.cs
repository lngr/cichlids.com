using System.Globalization;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

/// <summary>
/// Reads the care plan of a tank with the due state of each rule and changes its rules. A kind
/// without a stored rule falls back to the default rule of the domain. Every change of the rhythm
/// or of the switch is added to the history of the rule.
/// </summary>
public sealed class CarePlanService(CichlidsDbContext context, TimeProvider clock)
{
    private static readonly CareKind[] Kinds = [CareKind.WaterChange, CareKind.Measurement];

    /// <summary>
    /// Reads the local calendar date (YYYY-MM-DD) and the optional offset of the member's clock
    /// from UTC in minutes; null when either is not valid.
    /// </summary>
    public static (DateOnly Date, int UtcOffsetMinutes)? ParseLocalTime(string? date, int? utcOffsetMinutes)
    {
        var offset = utcOffsetMinutes ?? 0;
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && CarePlans.IsValidUtcOffset(offset) ? (parsed, offset) : null;
    }

    /// <summary>
    /// Reads the plan of a tank. The due state follows the entries the reader sees: a reader other than
    /// the owner does not see entries marked only me, so they do not move a due date.
    /// </summary>
    public async Task<CarePlanDto> GetPlanAsync(long tankId, DateOnly today, int utcOffsetMinutes, CancellationToken ct, bool includeOnlyMe = true)
    {
        var stored = await context.CarePlanRules.AsNoTracking().Where(r => r.TankId == tankId).ToListAsync(ct);
        var last = await LastOccurrencesAsync(context.CareEntries.Where(e => e.TankId == tankId && (includeOnlyMe || !e.OnlyMe)), ct);
        var reminders = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId).Select(t => t.CareRemindersEnabled).SingleOrDefaultAsync(ct);
        return BuildPlan(tankId, stored, last.ToDictionary(l => l.Kind, l => l.Last), today, utcOffsetMinutes, reminders);
    }

    /// <summary>
    /// Switches the reminders of a tank on or off for every device of its owner. Switching to the
    /// state the tank already has records nothing.
    /// </summary>
    public async Task<(CarePlanDto? Plan, string? Error)> ChangeRemindersAsync(long tankId, long profileId,
        UpdateCarePlanRequest request, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "care_not_found");
        if (request.RemindersEnabled is not { } enabled) return (null, "care_plan_invalid");
        var tank = await context.Tanks.SingleAsync(t => t.Id == tankId, ct);
        if (tank.CareRemindersEnabled != enabled)
        {
            var now = clock.GetUtcNow();
            tank.CareRemindersEnabled = enabled;
            context.OutboxEvents.Add(OutboxEvent.Create("tank.care_reminders_changed", "tank", tankId.ToString(),
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["tankId"] = tankId, ["profileId"] = profileId, ["enabled"] = enabled, ["changedAt"] = now,
                }), now));
            await context.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (await GetPlanAsync(tankId, today, utcOffsetMinutes, ct), null);
    }

    public async Task<(CarePlanDto? Plan, string? Error)> ChangeRuleAsync(long tankId, long profileId, string kindName,
        UpdateCarePlanRuleRequest request, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return (null, "care_not_found");
        if (!SnakeCaseEnum.TryParse<CareKind>(kindName, out var kind)) return (null, "care_kind_invalid");
        if (request.RhythmDays is null && request.Enabled is null) return (null, "care_plan_invalid");
        if (request.RhythmDays is { } days && !CarePlans.IsValidRhythm(days)) return (null, "care_plan_invalid");

        var stored = await context.CarePlanRules.SingleOrDefaultAsync(r => r.TankId == tankId && r.Kind == kind, ct);
        var current = stored is null ? CarePlans.DefaultFor(kind) : ToRule(stored);
        var now = clock.GetUtcNow();
        if (current is null)
        {
            if (request.RhythmDays is null) return request.Enabled == true
                ? (null, "care_plan_invalid") : (await GetPlanAsync(tankId, today, utcOffsetMinutes, ct), null);
            stored = new CarePlanRule { TankId = tankId, Kind = kind, RhythmDays = request.RhythmDays.Value, Enabled = request.Enabled ?? true,
                ReasonCode = CareReasons.MemberChoice, UpdatedAt = now };
            context.CarePlanRules.Add(stored);
        }
        else
        {
            var rhythmChanged = request.RhythmDays is { } requested && requested != current.RhythmDays;
            stored ??= context.CarePlanRules.Add(new CarePlanRule { TankId = tankId, Kind = kind }).Entity;
            stored.RhythmDays = request.RhythmDays ?? current.RhythmDays;
            stored.Enabled = request.Enabled ?? current.Enabled;
            stored.ReasonCode = rhythmChanged ? CareReasons.MemberChoice : current.ReasonCode;
            stored.ReasonParametersJson = rhythmChanged ? null : SerializeParameters(current.ReasonParameters);
            stored.UpdatedAt = now;
        }
        await context.SaveChangesAsync(ct);
        if (current is null || current.RhythmDays != stored.RhythmDays || current.Enabled != stored.Enabled)
            context.CarePlanRuleChanges.Add(new CarePlanRuleChange
            {
                TankId = tankId, Kind = kind, RhythmDays = stored.RhythmDays, Enabled = stored.Enabled, At = now,
            });
        context.OutboxEvents.Add(OutboxEvent.Create("care_plan.updated", "care_plan", stored.Id.ToString(), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["carePlanRuleId"] = stored.Id, ["tankId"] = tankId, ["kind"] = SnakeCaseEnum.ToSnakeCase(kind),
            ["rhythmDays"] = stored.RhythmDays, ["enabled"] = stored.Enabled, ["reasonCode"] = stored.ReasonCode,
            ["reasonParameters"] = ParseParameters(stored.ReasonParametersJson), ["updatedAt"] = stored.UpdatedAt,
        }), now));
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await GetPlanAsync(tankId, today, utcOffsetMinutes, ct), null);
    }

    /// <summary>
    /// The enabled rules of all live tanks of a profile with their due state: overdue first (the
    /// longest overdue first), then due today, then due in a number of days (the nearest first),
    /// then never logged.
    /// </summary>
    public async Task<DueCareDto> ListDueAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var tanks = await context.Tanks.AsNoTracking().Where(t => t.ProfileId == profileId && t.DeletedAt == null)
            .OrderBy(t => t.Id).Select(t => new { t.Id, t.Title, t.CareRemindersEnabled }).ToListAsync(ct);
        var ids = tanks.Select(t => t.Id).ToList();
        var stored = await context.CarePlanRules.AsNoTracking().Where(r => ids.Contains(r.TankId)).ToListAsync(ct);
        var last = await LastOccurrencesAsync(context.CareEntries.Where(e => ids.Contains(e.TankId)), ct);
        var items = new List<DueCareItemDto>();
        foreach (var tank in tanks)
        {
            var plan = BuildPlan(tank.Id, stored.Where(r => r.TankId == tank.Id).ToList(),
                last.Where(l => l.TankId == tank.Id).ToDictionary(l => l.Kind, l => l.Last), today, utcOffsetMinutes, tank.CareRemindersEnabled);
            items.AddRange(plan.Rules.Where(r => r is { Enabled: true, Due: not null, RhythmDays: not null, Reason: not null })
                .Select(r => new DueCareItemDto(tank.Id, tank.Title, r.Kind, r.RhythmDays!.Value, r.Reason!, r.Due!, tank.CareRemindersEnabled)));
        }
        var ordered = items.OrderBy(i => i.Due.State switch
            {
                CareDueState.Overdue => 0, CareDueState.DueToday => 1, CareDueState.DueIn => 2, _ => 3,
            })
            .ThenBy(i => i.Due.State == CareDueState.Overdue ? -i.Due.Days : i.Due.Days)
            .ThenBy(i => i.TankId).ThenBy(i => i.Kind).ToList();
        return new DueCareDto(ordered);
    }

    private static async Task<List<(long TankId, CareKind Kind, DateTimeOffset Last)>> LastOccurrencesAsync(
        IQueryable<CareEntry> entries, CancellationToken ct)
    {
        var rows = await entries.GroupBy(e => new { e.TankId, e.Kind })
            .Select(g => new { g.Key.TankId, g.Key.Kind, Last = g.Max(e => e.OccurredAt) }).ToListAsync(ct);
        return rows.Select(r => (r.TankId, r.Kind, r.Last)).ToList();
    }

    private static CarePlanDto BuildPlan(long tankId, IReadOnlyCollection<CarePlanRule> stored,
        IReadOnlyDictionary<CareKind, DateTimeOffset> last, DateOnly today, int utcOffsetMinutes, bool remindersEnabled)
    {
        var rules = new List<CarePlanRuleDto>();
        foreach (var kind in Kinds)
        {
            var row = stored.FirstOrDefault(r => r.Kind == kind);
            var rule = row is null ? CarePlans.DefaultFor(kind) : ToRule(row);
            if (rule is null)
            {
                rules.Add(new CarePlanRuleDto(kind, null, false, true, null, null));
                continue;
            }
            var due = CarePlans.DueFor(rule, last.TryGetValue(kind, out var at) ? at : null, today, utcOffsetMinutes);
            rules.Add(new CarePlanRuleDto(kind, rule.RhythmDays, rule.Enabled, row is null,
                new CareReasonDto(rule.ReasonCode, rule.ReasonParameters),
                due is { } d ? new CareDueDto(d.State, d.Days, d.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null));
        }
        return new CarePlanDto(tankId, rules, remindersEnabled);
    }

    private static CareRule ToRule(CarePlanRule row) =>
        new(row.Kind, row.RhythmDays, row.Enabled, row.ReasonCode, ParseParameters(row.ReasonParametersJson));

    private static Dictionary<string, string>? ParseParameters(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);

    private static string? SerializeParameters(IReadOnlyDictionary<string, string>? parameters) =>
        parameters is null ? null : JsonSerializer.Serialize(parameters);
}
