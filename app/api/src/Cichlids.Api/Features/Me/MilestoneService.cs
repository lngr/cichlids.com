using Cichlids.Api.Features.Streaks;
using Cichlids.Domain;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// The milestones of a member on Today and their dismissals. A milestone is listed from the day it is
/// reached for a week, until the member dismisses it. A dismissal is stored per member and key, so a
/// dismissed milestone is not listed.
/// </summary>
public sealed class MilestoneService(CichlidsDbContext context, StreakService streaks, TimeProvider clock)
{
    public const string KeyInvalidCode = "milestone_key_invalid";
    public const int MaxKeyLength = 64;
    public const string NotFoundCode = "milestone_not_found";

    public async Task<MilestonesDto> ListAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        var reached = await streaks.GetMilestonesAsync(profileId, today, utcOffsetMinutes, cancellationToken);
        if (reached.Count == 0) return new MilestonesDto([]);
        var dismissed = (await context.MilestoneDismissals.AsNoTracking().Where(d => d.ProfileId == profileId)
            .Select(d => d.Key).ToListAsync(cancellationToken)).ToHashSet();
        return new MilestonesDto(reached
            .Select(m => new MilestoneDto(Milestones.Key(m.Kind, m.TankId, m.Value), m.Kind, m.TankId, m.TankTitle, m.Value, StreakService.Format(m.ReachedOn)))
            .Where(m => !dismissed.Contains(m.Key)).ToList());
    }

    /// <summary>
    /// Stores the dismissal of a milestone of an own tank; a second dismissal of the same key changes nothing.
    /// A key that differs from its canonical form, is longer than the column or names an anniversary the
    /// tank has not completed is refused as invalid.
    /// The result is a problem code, or null when the milestone is dismissed.
    /// </summary>
    public async Task<string?> DismissAsync(long profileId, string key, CancellationToken cancellationToken)
    {
        if (key.Length > MaxKeyLength || Milestones.TryParseKey(key) is not { } parts
            || Milestones.Key(parts.Kind, parts.TankId, parts.Value) != key) return KeyInvalidCode;
        if (!await context.Tanks.AsNoTracking().AnyAsync(t => t.Id == parts.TankId && t.ProfileId == profileId && t.DeletedAt == null, cancellationToken))
            return NotFoundCode;
        // A day beyond the server date covers every clock of the world.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);
        if (parts.Kind == MilestoneKind.TankAnniversary && parts.Value > await streaks.CompletedTankYearsAsync(parts.TankId, today, cancellationToken))
            return KeyInvalidCode;
        var now = clock.GetUtcNow();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO public.milestone_dismissal (profile_id, key, dismissed_at) VALUES ({profileId}, {key}, {now}) ON CONFLICT (profile_id, key) DO NOTHING",
            cancellationToken);
        return null;
    }
}
