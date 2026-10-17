using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// Builds the return retrospective of a member and stores that the member saw it. A return
/// retrospective is due when an absence is recorded (away_since), no return retrospective was seen within
/// 91 days and no archive retrospective is due. After it was seen it stays readable for 30 days, covering
/// the absence it was shown for. Every card holds only what the member may see: the followed tanks come
/// from the visibility rules of the followed tanks section, the region news from public content, and the
/// responses and overdue care from the content of the member. A card without data is left out and
/// without any card there is no retrospective. A return retrospective belongs to the first app start of a
/// return: when the read finds a recorded absence it cannot show (the quarter is blocked or no card has
/// data), the same read clears the absence, so it does not open at a later start.
/// </summary>
public sealed class ReturnRetrospectiveService(
    CichlidsDbContext context, CarePlanService carePlans, FollowedTanksService followed,
    IArchiveRetrospectiveGate archive, IObjectStore objectStore, TimeProvider clock)
{
    public static readonly TimeSpan Quarter = TimeSpan.FromDays(91);
    public static readonly TimeSpan SeenWindow = TimeSpan.FromDays(30);
    public const int CardLimit = 5;
    public const int OverdueLimit = 10;

    private sealed record State(DateTimeOffset? AwaySince, DateTimeOffset? SeenAt, DateTimeOffset? Since, string[] Interests);

    private static bool QuarterFree(State state, DateTimeOffset now) =>
        state.SeenAt is not { } seen || seen <= now - Quarter;

    /// <summary>
    /// The due retrospective, or the one seen within the last 30 days, or null when there is none or no
    /// card has data.
    /// </summary>
    public async Task<ReturnRetrospectiveDto?> GetAsync(long profileId, DateOnly today, int utcOffsetMinutes, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await LoadStateAsync(profileId, cancellationToken);
        if (state is null) return null;

        var archiveDue = false;
        if (state.AwaySince is { } away && QuarterFree(state, now))
        {
            archiveDue = await archive.IsDueAsync(profileId, cancellationToken);
            if (!archiveDue)
            {
                var due = await BuildAsync(profileId, state.Interests, away, false, today, utcOffsetMinutes, now, cancellationToken);
                if (due is not null) return due;
            }
        }

        // The absence lapses unless an archive retrospective that is due takes precedence.
        if (state.AwaySince is { } lapsing && !archiveDue)
            await context.Profiles.Where(p => p.Id == profileId && p.AwaySince == lapsing)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AwaySince, (DateTimeOffset?)null), cancellationToken);

        if (state.SeenAt is { } seenAt && seenAt >= now - SeenWindow && state.Since is { } seenSince)
            return await BuildAsync(profileId, state.Interests, seenSince, true, today, utcOffsetMinutes, now, cancellationToken);
        return null;
    }

    /// <summary>
    /// Whether a return retrospective can be opened, read from the profile alone: one that is due, or one seen
    /// within the last 30 days. SeenAt is the time the member saw it, null while it is unseen.
    /// </summary>
    public async Task<ReturnRetrospectiveStatusDto> GetStatusAsync(long profileId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await LoadStateAsync(profileId, cancellationToken);
        if (state is null) return new ReturnRetrospectiveStatusDto(false, null);
        if (state.AwaySince is not null && QuarterFree(state, now) && !await archive.IsDueAsync(profileId, cancellationToken))
            return new ReturnRetrospectiveStatusDto(true, null);
        if (state.SeenAt is { } seenAt && seenAt >= now - SeenWindow && state.Since is not null)
            return new ReturnRetrospectiveStatusDto(true, seenAt);
        return new ReturnRetrospectiveStatusDto(false, null);
    }

    /// <summary>
    /// Stores that the member saw the due retrospective and clears the recorded absence. Does nothing when
    /// none is due.
    /// </summary>
    public async Task MarkSeenAsync(long profileId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await LoadStateAsync(profileId, cancellationToken);
        if (state?.AwaySince is null || !QuarterFree(state, now) || await archive.IsDueAsync(profileId, cancellationToken)) return;

        var quarterAgo = now - Quarter;
        await context.Profiles
            .Where(p => p.Id == profileId && p.AwaySince != null && (p.ReturnRetrospectiveSeenAt == null || p.ReturnRetrospectiveSeenAt <= quarterAgo))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ReturnRetrospectiveSince, p => p.AwaySince)
                .SetProperty(p => p.ReturnRetrospectiveSeenAt, now)
                .SetProperty(p => p.AwaySince, (DateTimeOffset?)null), cancellationToken);
    }

    private Task<State?> LoadStateAsync(long profileId, CancellationToken cancellationToken) =>
        context.Profiles.AsNoTracking().Where(p => p.Id == profileId)
            .Select(p => new State(p.AwaySince, p.ReturnRetrospectiveSeenAt, p.ReturnRetrospectiveSince, p.Interests))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<ReturnRetrospectiveDto?> BuildAsync(
        long profileId, string[] interests, DateTimeOffset since, bool seen, DateOnly today, int utcOffsetMinutes, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = await carePlans.ListDueAsync(profileId, today, utcOffsetMinutes, cancellationToken);
        var overdueItems = due.Items.Where(i => i.Due.State == CareDueState.Overdue).ToList();
        var overdue = overdueItems.Count == 0 ? null : new ReturnOverdueCareDto(overdueItems.Take(OverdueLimit)
            .Select(i => new ReturnOverdueCareItemDto(i.TankId, i.TankTitle, i.Kind, i.Due.Days)).ToList());
        var responses = await ResponsesAsync(profileId, since, cancellationToken);
        var followedUpdates = await followed.GetSinceAsync(profileId, since, cancellationToken);
        var followedTanks = followedUpdates.Items.Count == 0 ? null : followedUpdates;
        var regionNews = await RegionNewsAsync(profileId, interests, since, now, cancellationToken);

        if (overdue is null && responses is null && followedTanks is null && regionNews is null) return null;

        var careTanks = overdueItems.GroupBy(i => i.TankId).Select(g => new ReturnTankRefDto(g.Key, g.First().TankTitle)).ToList();
        var timeLapseTanks = await TimeLapseTanksAsync(profileId, cancellationToken);
        return new ReturnRetrospectiveDto(seen, since, overdue, responses, followedTanks, regionNews,
            new ReturnNextStepsDto(careTanks, timeLapseTanks));
    }

    private async Task<ReturnResponsesDto?> ResponsesAsync(long profileId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var members = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        var visibleTanks = context.TanksVisibleTo(profileId).Where(t => t.DeletedAt == null);
        var kudos = await context.CommentVotes.AsNoTracking().CountAsync(v => v.Value > 0 && v.CreatedAt >= since && v.ProfileId != profileId
            && context.Comments.Any(c => c.Id == v.CommentId && c.AuthorProfileId == profileId && c.DeletedAt == null
                && ((c.StoryId != null && context.Stories.Any(s => s.Id == c.StoryId && s.State == StoryState.Published && s.DeletedAt == null))
                    || (c.TankId != null && visibleTanks.Any(t => t.Id == c.TankId)))), cancellationToken);
        var comments = await context.Comments.AsNoTracking().CountAsync(c => c.DeletedAt == null && c.CreatedAt >= since && c.AuthorProfileId != profileId
            && (c.AuthorProfileId == null || members.Any(a => a.Id == c.AuthorProfileId))
            && ((c.StoryId != null && context.Stories.Any(s => s.Id == c.StoryId && s.AuthorProfileId == profileId && s.State == StoryState.Published && s.DeletedAt == null))
                || (c.TankId != null && context.Tanks.Any(t => t.Id == c.TankId && t.ProfileId == profileId && t.DeletedAt == null))), cancellationToken);
        return kudos + comments == 0 ? null : new ReturnResponsesDto(kudos, comments);
    }

    private async Task<ReturnRegionNewsDto?> RegionNewsAsync(
        long profileId, string[] interests, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var regions = interests.Select(i => SnakeCaseEnum.TryParse<TankRegion>(i, out var region) ? (TankRegion?)region : null)
            .Where(r => r is not null && r != TankRegion.AmericasUnassigned).Select(r => r!.Value).Distinct().ToList();
        if (regions.Count == 0) return null;
        if (regions.Contains(TankRegion.CentralAmerica) || regions.Contains(TankRegion.SouthAmerica)) regions.Add(TankRegion.AmericasUnassigned);

        var members = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        var publicTanks = context.TanksVisibleTo(null)
            .Where(t => t.State == TankState.Published && t.DeletedAt == null && members.Any(p => p.Id == t.ProfileId));
        var regional = publicTanks.Where(t => t.Region != null && regions.Contains(t.Region.Value));

        var tanks = await regional.AsNoTracking()
            .Where(t => t.ProfileId != profileId && (t.PublishedAt ?? t.CreatedAt) >= since)
            .OrderByDescending(t => t.PublishedAt ?? t.CreatedAt).ThenByDescending(t => t.Id).Take(CardLimit)
            .Select(t => new { t.Id, t.Title, t.Region, t.ProfileId }).ToListAsync(cancellationToken);
        var stories = await context.Stories.AsNoTracking()
            .Where(s => s.State == StoryState.Published && s.DeletedAt == null && s.AuthorProfileId != profileId
                && (s.PublishedAt ?? s.CreatedAt) >= since && members.Any(a => a.Id == s.AuthorProfileId)
                && context.StorySubjects.Any(x => x.StoryId == s.Id && x.TankId != null && regional.Any(t => t.Id == x.TankId))
                && !context.StorySubjects.Any(x => x.StoryId == s.Id && x.TankId != null && !publicTanks.Any(t => t.Id == x.TankId)))
            .OrderByDescending(s => s.PublishedAt ?? s.CreatedAt).ThenByDescending(s => s.Id).Take(CardLimit)
            .Select(s => new { s.Id, s.Title, s.AuthorProfileId }).ToListAsync(cancellationToken);
        if (tanks.Count + stories.Count == 0) return null;

        var storyIds = stories.Select(s => s.Id).ToList();
        var slugs = await context.SlugAliases.AsNoTracking().Where(s => s.IsCanonical && storyIds.Contains(s.StoryId))
            .ToDictionaryAsync(s => s.StoryId, s => s.Value, cancellationToken);
        var authorIds = tanks.Select(t => t.ProfileId).Concat(stories.Select(s => s.AuthorProfileId)).Distinct().ToList();
        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);
        return new ReturnRegionNewsDto(
            tanks.Select(t => new ReturnRegionTankDto(t.Id, t.Title, t.Region, authors[t.ProfileId].ToDto(objectStore))).ToList(),
            stories.Select(s => new ReturnRegionStoryDto(s.Id, slugs.GetValueOrDefault(s.Id), s.Title, authors[s.AuthorProfileId].ToDto(objectStore))).ToList());
    }

    private async Task<List<ReturnTankRefDto>> TimeLapseTanksAsync(long profileId, CancellationToken cancellationToken) =>
        await context.Tanks.AsNoTracking()
            .Where(t => t.ProfileId == profileId && t.DeletedAt == null
                && context.MediaLinks.Any(l => l.TankId == t.Id && l.ContextType == MediaLinkContext.Tank && l.Role == MediaLinkRole.TimeLapse))
            .OrderByDescending(t => context.MediaLinks.Where(l => l.TankId == t.Id && l.Role == MediaLinkRole.TimeLapse).Max(l => l.CreatedAt))
            .ThenByDescending(t => t.Id).Take(CardLimit)
            .Select(t => new ReturnTankRefDto(t.Id, t.Title)).ToListAsync(cancellationToken);
}
