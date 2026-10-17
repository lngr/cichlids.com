using Cichlids.Api.Features.Streaks;
using Cichlids.Domain;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// The milestones of a member on Today and their dismissals. A milestone is listed from the day it is
/// reached for a week, until the member dismisses it. A dismissal is stored per member and key, so a
/// dismissed milestone is not listed. Milestones come from the live tanks of the member and from the
/// anniversaries of the named fish that live in them.
/// </summary>
public sealed class MilestoneService(CichlidsDbContext context, StreakService streaks, TimeProvider clock)
{
    public const string KeyInvalidCode = "milestone_key_invalid";
    public const int MaxKeyLength = 64;
    public const string NotFoundCode = "milestone_not_found";

    public async Task<MilestonesDto> ListAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        var reached = await streaks.GetMilestonesAsync(profileId, today, utcOffsetMinutes, cancellationToken);
        var items = reached
            .Select(m => new MilestoneDto(Milestones.Key(m.Kind, m.TankId, m.Value), m.Kind, m.TankId, m.TankTitle, m.Value, StreakService.Format(m.ReachedOn), null, null))
            .ToList();
        items.AddRange(await FishAnniversariesAsync(profileId, today, cancellationToken));
        if (items.Count == 0) return new MilestonesDto([]);
        var dismissed = (await context.MilestoneDismissals.AsNoTracking().Where(d => d.ProfileId == profileId)
            .Select(d => d.Key).ToListAsync(cancellationToken)).ToHashSet();
        return new MilestonesDto(items.Where(m => !dismissed.Contains(m.Key)).OrderByDescending(m => m.ReachedOn, StringComparer.Ordinal).ToList());
    }

    // The anniversaries of the current named fish of the live tanks of the member that were reached within the visible days.
    private async Task<List<MilestoneDto>> FishAnniversariesAsync(long profileId, DateOnly today, CancellationToken cancellationToken)
    {
        var fish = await context.Inhabitants.AsNoTracking()
            .Where(i => i.OwnerProfileId == profileId && i.Name != null && i.RemovedOn == null && i.MovedInOn != null)
            .Join(context.Tanks.Where(t => t.DeletedAt == null), i => i.TankId, t => t.Id,
                (i, t) => new { i.Id, Name = i.Name!, MovedInOn = i.MovedInOn!.Value, TankId = t.Id, t.Title })
            .OrderBy(i => i.Id).ToListAsync(cancellationToken);
        var windowStart = today.AddDays(1 - Milestones.VisibleDays);
        var result = new List<MilestoneDto>();
        foreach (var item in fish)
        {
            foreach (var (year, reached) in Milestones.FishAnniversaries(item.MovedInOn, windowStart, today))
                result.Add(new MilestoneDto(Milestones.Key(MilestoneKind.FishAnniversary, item.Id, year), MilestoneKind.FishAnniversary, item.TankId, item.Title,
                    year, StreakService.Format(reached), item.Id, item.Name));
        }

        return result;
    }

    /// <summary>
    /// Stores the dismissal of a milestone of an own tank or an own named fish; a second dismissal of the same
    /// key changes nothing. A key that differs from its canonical form, is longer than the column or names an
    /// anniversary that is not completed yet is refused as invalid.
    /// The result is a problem code, or null when the milestone is dismissed.
    /// </summary>
    public async Task<string?> DismissAsync(long profileId, string key, CancellationToken cancellationToken)
    {
        if (key.Length > MaxKeyLength || Milestones.TryParseKey(key) is not { } parts
            || Milestones.Key(parts.Kind, parts.SubjectId, parts.Value) != key) return KeyInvalidCode;
        // A day beyond the server date covers every clock of the world.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);
        if (parts.Kind == MilestoneKind.FishAnniversary)
        {
            var movedIn = await context.Inhabitants.AsNoTracking()
                .Where(i => i.Id == parts.SubjectId && i.OwnerProfileId == profileId && i.Name != null)
                .Select(i => new { i.MovedInOn }).SingleOrDefaultAsync(cancellationToken);
            if (movedIn is null) return NotFoundCode;
            if (movedIn.MovedInOn is not { } day || parts.Value > Milestones.CompletedFishYears(day, today)) return KeyInvalidCode;
        }
        else
        {
            if (!await context.Tanks.AsNoTracking().AnyAsync(t => t.Id == parts.SubjectId && t.ProfileId == profileId && t.DeletedAt == null, cancellationToken))
                return NotFoundCode;
            if (parts.Kind == MilestoneKind.TankAnniversary && parts.Value > await streaks.CompletedTankYearsAsync(parts.SubjectId, today, cancellationToken))
                return KeyInvalidCode;
        }

        var now = clock.GetUtcNow();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO public.milestone_dismissal (profile_id, key, dismissed_at) VALUES ({profileId}, {key}, {now}) ON CONFLICT (profile_id, key) DO NOTHING",
            cancellationToken);
        return null;
    }
}
