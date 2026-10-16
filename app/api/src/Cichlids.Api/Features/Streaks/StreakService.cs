using System.Globalization;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Streaks;

/// <summary>
/// Reads the streaks of tanks from their water changes, series photos and plan history together with the
/// vacations of the owner. The streaks count every water change of the tank, also the ones marked only me,
/// so all viewers see the same numbers. A read by the owner records the freezes the evaluation consumed,
/// once per water change. Viewers other than the owner get counts only: no pause, no deadline, no date of the
/// last water change and no active vacation.
/// </summary>
public sealed class StreakService(CichlidsDbContext context, TimeProvider clock, IOptions<StreakOptions> options)
{
    public const string NotFoundCode = "streak_not_found";
    private const string DateFormat = "yyyy-MM-dd";

    private sealed record TankHistory(DateTimeOffset TankCreatedAt, List<WaterChangeRecord> Changes, List<RhythmChange> Rhythm, HashSet<long> Recorded, List<SeriesPhoto> Photos,
        List<DecommissionSpan> Decommissions);

    private int Freezes => options.Value.FreezesPerYear;

    /// <summary>
    /// Reads the local date and the optional UTC offset a client sends. Null when the date is not a
    /// calendar date, the offset is outside the range of clocks, or the date differs from the date of
    /// the server by more than the allowed skew.
    /// </summary>
    public (DateOnly Date, int UtcOffsetMinutes)? ParseLocalTime(string? date, int? utcOffsetMinutes)
    {
        if (CarePlanService.ParseLocalTime(date, utcOffsetMinutes) is not var (local, offset)) return null;
        var serverToday = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Math.Abs(local.DayNumber - serverToday.DayNumber) <= options.Value.MaxClientDateSkewDays ? (local, offset) : null;
    }

    /// <summary>
    /// Whether adding a vacation would repair what has already ended: a period of a water change
    /// streak that was missed or unverified without a freeze, or a week of a time-lapse streak without
    /// a photo, in any live tank of the member. A vacation that starts within the repair window never
    /// counts, since the days just passed are still the member's to pause.
    /// </summary>
    public async Task<bool> WouldRepairAsync(long profileId, VacationSpan candidate, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        if (candidate.Start >= today.AddDays(-options.Value.RepairWindowDays)) return false;
        var tankIds = await context.Tanks.AsNoTracking().Where(t => t.ProfileId == profileId && t.DeletedAt == null).Select(t => t.Id).ToListAsync(ct);
        var histories = await LoadAsync(tankIds, ct);
        var before = (await LoadVacationsAsync(profileId, ct)).Select(v => v.Span).ToList();
        var after = before.Append(candidate).ToList();
        foreach (var history in histories.Values)
        {
            var water = (Before: EvaluateWater(history, before, today, utcOffsetMinutes), After: EvaluateWater(history, after, today, utcOffsetMinutes));
            if (water.Before.State == WaterChangeStreakState.Broken && water.After.State != WaterChangeStreakState.Broken) return true;
            for (var i = 0; i < water.Before.Periods.Count; i++)
            {
                var was = water.Before.Periods[i].Outcome;
                if (was is PeriodOutcome.Missed or PeriodOutcome.Unverified && water.After.Periods[i].Outcome != was) return true;
            }
            var lapseBefore = TimeLapseStreaks.Evaluate(history.Photos, utcOffsetMinutes, before, today);
            var lapseAfter = TimeLapseStreaks.Evaluate(history.Photos, utcOffsetMinutes, after, today);
            if (lapseAfter.Current > lapseBefore.Current || lapseAfter.Longest > lapseBefore.Longest) return true;
        }
        return false;
    }

    /// <summary>
    /// The streaks of a tank for a viewer who sees at least the tank profile.
    /// </summary>
    public async Task<TankStreaksDto> GetTankAsync(TankAccess access, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var owner = access.IsOwner;
        var enabled = await StreaksEnabledAsync(access.OwnerProfileId, ct);
        if (!enabled && !owner) return new TankStreaksDto(access.TankId, null, null, []);
        var history = (await LoadAsync([access.TankId], ct))[access.TankId];
        var spans = await LoadVacationsAsync(access.OwnerProfileId, ct);
        WaterChangeStreakDto? water = null;
        if (access.CanSeeStream)
        {
            var streak = EvaluateWater(history, spans.Select(s => s.Span).ToList(), today, utcOffsetMinutes);
            if (owner) await RecordFreezesAsync(access.TankId, access.OwnerProfileId, streak.Freezes.Where(f => !f.Recorded).ToList(), ct);
            water = ToDto(streak, owner);
        }
        if (!enabled) return new TankStreaksDto(access.TankId, null, null, []);
        var timeLapse = ToDto(TimeLapseStreaks.Evaluate(history.Photos, utcOffsetMinutes, spans.Select(s => s.Span).ToList(), today), owner);
        var shown = owner ? spans : spans.Where(s => s.Span.End is not null).ToList();
        return new TankStreaksDto(access.TankId, water, timeLapse, shown.Select(ToDto).ToList());
    }

    /// <summary>
    /// The streaks of every live tank of the member. With the streaks switched off the evaluation runs and
    /// records the freezes it consumes, and the result is empty.
    /// </summary>
    public async Task<MeStreaksDto> GetOwnAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var tanks = await context.Tanks.AsNoTracking().Where(t => t.ProfileId == profileId && t.DeletedAt == null)
            .OrderBy(t => t.Id).Select(t => new { t.Id, t.Title }).ToListAsync(ct);
        var enabled = await StreaksEnabledAsync(profileId, ct);
        var histories = await LoadAsync(tanks.Select(t => t.Id).ToList(), ct);
        var vacations = await LoadVacationsAsync(profileId, ct);
        var spans = vacations.Select(v => v.Span).ToList();
        var result = new List<OwnTankStreaksDto>();
        foreach (var tank in tanks)
        {
            var history = histories[tank.Id];
            var streak = EvaluateWater(history, spans, today, utcOffsetMinutes);
            await RecordFreezesAsync(tank.Id, profileId, streak.Freezes.Where(f => !f.Recorded).ToList(), ct);
            if (!enabled) continue;
            result.Add(new OwnTankStreaksDto(tank.Id, tank.Title, ToDto(streak, true),
                ToDto(TimeLapseStreaks.Evaluate(history.Photos, utcOffsetMinutes, spans, today), true)));
        }
        return new MeStreaksDto(enabled, Freezes, enabled ? vacations.Select(ToDto).ToList() : [], result);
    }

    /// <summary>
    /// The reminder window of the water change streak of each given tank, from the rhythm of its water
    /// change rule. A running streak has the days from the first reminder day through its deadline; after
    /// a vacation the first reminder day is the day after the vacation when that is later. A streak paused
    /// by a vacation has no days, and every other tank is left out. A member with the streaks switched off
    /// has no windows. Nothing is recorded.
    /// </summary>
    public async Task<Dictionary<long, DueStreakDto>> GetReminderWindowsAsync(long profileId, IReadOnlyDictionary<long, int> rhythmByTank,
        DateOnly today, int utcOffsetMinutes, CancellationToken ct)
    {
        var windows = new Dictionary<long, DueStreakDto>();
        if (rhythmByTank.Count == 0 || !await StreaksEnabledAsync(profileId, ct)) return windows;
        var histories = await LoadAsync(rhythmByTank.Keys.ToList(), ct, includePhotos: false);
        var spans = (await LoadVacationsAsync(profileId, ct)).Select(v => v.Span).ToList();
        foreach (var (tankId, rhythmDays) in rhythmByTank)
        {
            var streak = EvaluateWater(histories[tankId], spans, today, utcOffsetMinutes);
            if (streak is { State: WaterChangeStreakState.Running, Deadline: { } deadline })
            {
                var first = WaterChangeStreaks.FirstReminderDay(deadline, rhythmDays);
                if (streak.FirstDayAfterVacation is { } afterVacation && afterVacation > first) first = afterVacation;
                windows[tankId] = new DueStreakDto(streak.State, Format(first), Format(deadline));
            }
            else if (streak.State == WaterChangeStreakState.Paused)
                windows[tankId] = new DueStreakDto(streak.State, null, null);
        }
        return windows;
    }

    private async Task<bool> StreaksEnabledAsync(long profileId, CancellationToken ct) =>
        await context.Profiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.StreaksEnabled).SingleAsync(ct);

    private WaterChangeStreak EvaluateWater(TankHistory history, IReadOnlyList<VacationSpan> spans, DateOnly today, int utcOffsetMinutes) =>
        WaterChangeStreaks.Evaluate(history.Changes, history.Rhythm, spans, history.Recorded, Freezes,
            CarePlans.LocalDate(history.TankCreatedAt, utcOffsetMinutes), today, utcOffsetMinutes, history.Decommissions);

    private async Task<Dictionary<long, TankHistory>> LoadAsync(IReadOnlyCollection<long> tankIds, CancellationToken ct, bool includePhotos = true)
    {
        var created = await context.Tanks.AsNoTracking().Where(t => tankIds.Contains(t.Id)).Select(t => new { t.Id, t.CreatedAt }).ToListAsync(ct);
        var result = created.ToDictionary(t => t.Id, t => new TankHistory(t.CreatedAt, [], [], [], [], []));
        var changes = await context.CareEntries.AsNoTracking().Where(e => tankIds.Contains(e.TankId) && e.Kind == CareKind.WaterChange)
            .Select(e => new { e.TankId, e.Id, e.OccurredAt, Verified = e.Evidence == WaterChangeEvidenceState.Verified }).ToListAsync(ct);
        foreach (var change in changes) result[change.TankId].Changes.Add(new WaterChangeRecord(change.Id, change.OccurredAt, change.Verified));
        var rhythm = await context.CarePlanRuleChanges.AsNoTracking().Where(c => tankIds.Contains(c.TankId) && c.Kind == CareKind.WaterChange)
            .OrderBy(c => c.At).ThenBy(c => c.Id).ToListAsync(ct);
        foreach (var change in rhythm) result[change.TankId].Rhythm.Add(new RhythmChange(change.At, change.RhythmDays, change.Enabled));
        var freezes = await context.StreakFreezes.AsNoTracking().Where(f => tankIds.Contains(f.TankId)).Select(f => new { f.TankId, f.CareEntryId }).ToListAsync(ct);
        foreach (var freeze in freezes) result[freeze.TankId].Recorded.Add(freeze.CareEntryId);
        var decommissions = await context.TankDecommissionings.AsNoTracking().Where(d => tankIds.Contains(d.TankId))
            .OrderBy(d => d.StartDate).Select(d => new { d.TankId, d.StartDate, d.EndDate }).ToListAsync(ct);
        foreach (var decommissioning in decommissions) result[decommissioning.TankId].Decommissions.Add(new DecommissionSpan(decommissioning.StartDate, decommissioning.EndDate));
        if (!includePhotos) return result;
        var photos = await context.TankMedia.AsNoTracking().Where(m => tankIds.Contains(m.TankId) && m.Section == TankMediaSection.TimeLapse && m.CapturedInApp)
            .Join(context.MediaItems, m => m.MediaItemId, i => i.Id, (m, i) => new { m.TankId, i.Id, i.CreatedAt }).ToListAsync(ct);
        foreach (var photo in photos) result[photo.TankId].Photos.Add(new SeriesPhoto(photo.Id, photo.CreatedAt));
        return result;
    }

    private async Task<List<(long Id, VacationSpan Span)>> LoadVacationsAsync(long profileId, CancellationToken ct) =>
        (await context.Vacations.AsNoTracking().Where(v => v.ProfileId == profileId).OrderBy(v => v.StartDate).ThenBy(v => v.Id).ToListAsync(ct))
            .Select(v => (v.Id, new VacationSpan(v.StartDate, v.EndDate))).ToList();

    /// <summary>
    /// Stores the freezes of the evaluation that are not recorded yet. The tank row is locked, so
    /// concurrent reads record a freeze once; a water change that is gone by then records nothing.
    /// </summary>
    private async Task RecordFreezesAsync(long tankId, long profileId, IReadOnlyList<FreezeUse> fresh, CancellationToken ct)
    {
        if (fresh.Count == 0) return;
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await CareTanks.LockOwnAsync(context, tankId, profileId, ct)) return;
        var ids = fresh.Select(f => f.WaterChangeId).ToList();
        var existing = await context.StreakFreezes.Where(f => ids.Contains(f.CareEntryId)).Select(f => f.CareEntryId).ToListAsync(ct);
        var present = await context.CareEntries.Where(e => ids.Contains(e.Id) && e.TankId == tankId).Select(e => e.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var freeze in fresh.Where(f => !existing.Contains(f.WaterChangeId) && present.Contains(f.WaterChangeId)))
        {
            var row = new StreakFreeze
            {
                TankId = tankId, CareEntryId = freeze.WaterChangeId, TankYear = freeze.TankYear, PeriodStart = freeze.PeriodStart,
                PeriodEnd = freeze.PeriodEnd, CreatedAt = now,
            };
            context.StreakFreezes.Add(row);
            await context.SaveChangesAsync(ct);
            context.OutboxEvents.Add(OutboxEvent.Create("streak.freeze_used", "streak_freeze", row.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["streakFreezeId"] = row.Id, ["tankId"] = tankId, ["profileId"] = profileId, ["careEntryId"] = row.CareEntryId,
                    ["tankYear"] = row.TankYear, ["periodStart"] = Format(row.PeriodStart), ["periodEnd"] = Format(row.PeriodEnd), ["usedAt"] = now,
                }), now));
        }
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private WaterChangeStreakDto ToDto(WaterChangeStreak streak, bool owner)
    {
        var state = !owner && streak.State == WaterChangeStreakState.Paused ? WaterChangeStreakState.Running : streak.State;
        return new WaterChangeStreakDto(state, streak.Current, streak.CurrentVerified, streak.Longest,
            owner && streak.LastWaterChange is { } last ? Format(last) : null,
            owner && streak.Deadline is { } deadline ? Format(deadline) : null,
            owner ? streak.FreezesLeft : null, owner ? Freezes : null);
    }

    private static TimeLapseStreakDto ToDto(TimeLapseStreak streak, bool owner) =>
        new(streak.Current, streak.Longest, streak.ThisWeekHasPhoto, owner ? streak.ThisWeekOpen : !streak.ThisWeekHasPhoto,
            Format(streak.WeekStart), Format(streak.WeekEnd), owner ? streak.Paused : null, owner ? streak.KeepingPhotoIds : null);

    public static VacationDto ToDto((long Id, VacationSpan Span) vacation) =>
        new(vacation.Id, Format(vacation.Span.Start), vacation.Span.End is { } end ? Format(end) : null, vacation.Span.End is null);
}
