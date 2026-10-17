using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

/// <summary>
/// Reads the tanks a member follows together with the newest update of each that the member may see.
/// A tank counts when it is published, its owner is a member who is not suspended, the follow is
/// approved and the profile level of the tank reaches the member. An update counts when the
/// visibility rules show it to the member: an entry of the care stream that is not marked only me, a
/// published story that points to the tank, and a tank or time-lapse photo whose media link the
/// media visibility rule shows. Updates, counts and the flag for more tanks come from visible data only.
/// </summary>
public sealed class FollowedTanksService(CichlidsDbContext context, IObjectStore objectStore, TimeProvider clock, MediaUrlSigner? signer = null)
{
    public const int TodayLimit = 5;
    public const int FreshDays = 30;

    private sealed class StoryRow
    {
        public long TankId { get; init; }
        public long StoryId { get; init; }
        public string? Title { get; init; }
        public DateTimeOffset At { get; init; }
    }

    private sealed class PhotoRow
    {
        public long TankId { get; init; }
        public long LinkId { get; init; }
        public long MediaItemId { get; init; }
        public bool TimeLapse { get; init; }
        public DateTimeOffset At { get; init; }
    }

    private sealed class CareRow
    {
        public long TankId { get; init; }
        public long EntryId { get; init; }
        public CareKind Kind { get; init; }
        public DateTimeOffset At { get; init; }
    }

    private sealed record Newest(long TankId, FollowedUpdateKind Kind, DateTimeOffset At);

    /// <summary>
    /// The followed tanks with a visible update in the last 30 days, newest update first, at most five.
    /// </summary>
    public Task<TodayFollowedDto> GetTodayAsync(long viewer, CancellationToken cancellationToken) =>
        GetSinceAsync(viewer, clock.GetUtcNow().AddDays(-FreshDays), cancellationToken);

    /// <summary>
    /// The followed tanks with a visible update since the given moment, newest update first, at most five.
    /// </summary>
    public async Task<TodayFollowedDto> GetSinceAsync(long viewer, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ids = await VisibleFollowedIdsAsync(viewer, now, cancellationToken);
        var newest = await NewestAsync(ids, viewer, now, since, cancellationToken);
        var ordered = newest.Values.OrderByDescending(n => n.At).ThenByDescending(n => n.TankId).ToList();
        var items = await ToDtosAsync(ordered.Take(TodayLimit).Select(n => (n.TankId, (Newest?)n)).ToList(), viewer, now, cancellationToken);
        return new TodayFollowedDto(items.Where(i => i.Update is not null).ToList(), ordered.Count > TodayLimit);
    }

    /// <summary>
    /// All followed tanks the member sees, those with the newest visible update first, then the ones without.
    /// </summary>
    public async Task<PagedResponse<FollowedTankDto>> ListAsync(long viewer, int offset, int limit, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ids = await VisibleFollowedIdsAsync(viewer, now, cancellationToken);
        var newest = await NewestAsync(ids, viewer, now, DateTimeOffset.UnixEpoch, cancellationToken);
        var page = ids.OrderByDescending(id => newest.TryGetValue(id, out var n) ? n.At : DateTimeOffset.MinValue).ThenByDescending(id => id)
            .Skip(offset).Take(limit).Select(id => (id, newest.GetValueOrDefault(id))).ToList();
        return new PagedResponse<FollowedTankDto>(ids.Count, await ToDtosAsync(page, viewer, now, cancellationToken));
    }

    private async Task<List<long>> VisibleFollowedIdsAsync(long viewer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var owners = context.Profiles.Where(p => p.Kind == ProfileKind.Member && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        return await context.TanksVisibleTo(viewer).AsNoTracking()
            .Where(t => t.State == TankState.Published && t.DeletedAt == null && t.ProfileId != viewer
                && context.Follows.Any(f => f.FollowerProfileId == viewer && f.TargetTankId == t.Id && f.Status == FollowStatus.Approved)
                && owners.Any(p => p.Id == t.ProfileId))
            .Select(t => t.Id).ToListAsync(cancellationToken);
    }

    private IQueryable<StoryRow> StoryUpdates(IReadOnlyCollection<long> tankIds, DateTimeOffset now, DateTimeOffset since) =>
        from subject in context.StorySubjects.AsNoTracking()
        join story in context.Stories on subject.StoryId equals story.Id
        where subject.SubjectType == StorySubjectType.Tank && tankIds.Contains(subject.TankId!.Value)
            && story.State == StoryState.Published && story.DeletedAt == null
            && (story.PublishedAt ?? story.CreatedAt) >= since
            && context.Profiles.Any(a => a.Id == story.AuthorProfileId && a.Kind == ProfileKind.Member && (a.SuspendedUntil == null || a.SuspendedUntil <= now))
        select new StoryRow { TankId = subject.TankId!.Value, StoryId = story.Id, Title = story.Title, At = story.PublishedAt ?? story.CreatedAt };

    private IQueryable<PhotoRow> PhotoUpdates(IReadOnlyCollection<long> tankIds, long viewer, DateTimeOffset now, DateTimeOffset since) =>
        context.MediaLinks.AsNoTracking().Where(MediaVisibility.LinkVisibleTo(context, viewer, now))
            .Where(l => l.ContextType == MediaLinkContext.Tank && tankIds.Contains(l.TankId!.Value) && l.CreatedAt >= since
                && l.MediaItem.Kind == MediaKind.Photo
                && (l.Role == MediaLinkRole.TimeLapse || l.Role == MediaLinkRole.Showcase || l.Role == MediaLinkRole.Decoration || l.Role == MediaLinkRole.Technic))
            .Select(l => new PhotoRow { TankId = l.TankId!.Value, LinkId = l.Id, MediaItemId = l.MediaItemId, TimeLapse = l.Role == MediaLinkRole.TimeLapse, At = l.CreatedAt });

    private IQueryable<CareRow> CareUpdates(IReadOnlyCollection<long> streamTankIds, DateTimeOffset since) =>
        context.CareEntries.AsNoTracking()
            .Where(e => streamTankIds.Contains(e.TankId) && !e.OnlyMe && e.RecordedAt >= since)
            .Select(e => new CareRow { TankId = e.TankId, EntryId = e.Id, Kind = e.Kind, At = e.RecordedAt });

    private async Task<List<long>> StreamVisibleAsync(IReadOnlyCollection<long> tankIds, long viewer, CancellationToken cancellationToken) =>
        await context.TanksStreamVisibleTo(viewer).Where(t => tankIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);

    private async Task<Dictionary<long, Newest>> NewestAsync(
        IReadOnlyCollection<long> tankIds, long viewer, DateTimeOffset now, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var best = new Dictionary<long, Newest>();
        if (tankIds.Count == 0) return best;

        // On equal moments the kind that comes first in the enum wins.
        void Offer(long tankId, FollowedUpdateKind kind, DateTimeOffset at)
        {
            if (!best.TryGetValue(tankId, out var current) || at > current.At || (at == current.At && kind < current.Kind))
                best[tankId] = new Newest(tankId, kind, at);
        }

        foreach (var row in await StoryUpdates(tankIds, now, since).GroupBy(r => r.TankId)
                     .Select(g => new { TankId = g.Key, At = g.Max(r => r.At) }).ToListAsync(cancellationToken))
            Offer(row.TankId, FollowedUpdateKind.Story, row.At);
        foreach (var row in await PhotoUpdates(tankIds, viewer, now, since).GroupBy(r => new { r.TankId, r.TimeLapse })
                     .Select(g => new { g.Key.TankId, g.Key.TimeLapse, At = g.Max(r => r.At) }).ToListAsync(cancellationToken))
            Offer(row.TankId, row.TimeLapse ? FollowedUpdateKind.TimeLapsePhoto : FollowedUpdateKind.TankPhoto, row.At);
        var streamTanks = await StreamVisibleAsync(tankIds, viewer, cancellationToken);
        foreach (var row in await CareUpdates(streamTanks, since).GroupBy(r => r.TankId)
                     .Select(g => new { TankId = g.Key, At = g.Max(r => r.At) }).ToListAsync(cancellationToken))
            Offer(row.TankId, FollowedUpdateKind.CareEntry, row.At);
        return best;
    }

    private async Task<List<FollowedTankDto>> ToDtosAsync(
        IReadOnlyList<(long TankId, Newest? Newest)> page, long viewer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (page.Count == 0) return [];
        var ids = page.Select(p => p.TankId).ToList();
        var tanks = await context.Tanks.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Title, t.ProfileId })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var owners = await AuthorBatchLoader.LoadAsync(context, tanks.Values.Select(t => t.ProfileId).Distinct().ToList(), cancellationToken);
        var updates = await DetailsAsync(page.Where(p => p.Newest is not null).Select(p => p.Newest!).ToList(), viewer, now, cancellationToken);
        return page.Select(p => new FollowedTankDto(
            p.TankId, tanks[p.TankId].Title, owners[tanks[p.TankId].ProfileId].ToDto(objectStore), updates.GetValueOrDefault(p.TankId))).ToList();
    }

    /// <summary>
    /// The details of the newest updates of a page, one query per kind for all tanks of the page. A tank
    /// whose update is gone when its details are read has no entry.
    /// </summary>
    private async Task<Dictionary<long, FollowedUpdateDto>> DetailsAsync(
        IReadOnlyList<Newest> winners, long viewer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, FollowedUpdateDto>();
        if (winners.Count == 0) return result;
        var since = winners.Min(w => w.At);
        var ats = winners.Select(w => w.At).Distinct().ToList();
        List<long> TanksOf(Func<Newest, bool> kind) => winners.Where(kind).Select(w => w.TankId).ToList();

        var storyTanks = TanksOf(w => w.Kind == FollowedUpdateKind.Story);
        if (storyTanks.Count > 0)
        {
            var rows = await StoryUpdates(storyTanks, now, since).Where(r => ats.Contains(r.At)).ToListAsync(cancellationToken);
            var slugs = await context.SlugAliases.AsNoTracking().Where(s => s.IsCanonical && rows.Select(r => r.StoryId).Contains(s.StoryId))
                .ToDictionaryAsync(s => s.StoryId, s => s.Value, cancellationToken);
            foreach (var winner in winners.Where(w => w.Kind == FollowedUpdateKind.Story))
            {
                var row = rows.Where(r => r.TankId == winner.TankId && r.At == winner.At).OrderByDescending(r => r.StoryId).FirstOrDefault();
                if (row is not null)
                    result[winner.TankId] = new FollowedUpdateDto(winner.Kind, winner.At, null, new FollowedStoryDto(row.StoryId, slugs.GetValueOrDefault(row.StoryId), row.Title), null);
            }
        }

        var careTanks = TanksOf(w => w.Kind == FollowedUpdateKind.CareEntry);
        if (careTanks.Count > 0)
        {
            var rows = await CareUpdates(await StreamVisibleAsync(careTanks, viewer, cancellationToken), since).Where(r => ats.Contains(r.At)).ToListAsync(cancellationToken);
            foreach (var winner in winners.Where(w => w.Kind == FollowedUpdateKind.CareEntry))
            {
                var row = rows.Where(r => r.TankId == winner.TankId && r.At == winner.At).OrderByDescending(r => r.EntryId).FirstOrDefault();
                if (row is not null) result[winner.TankId] = new FollowedUpdateDto(winner.Kind, winner.At, row.Kind, null, null);
            }
        }

        var photoTanks = TanksOf(w => w.Kind is FollowedUpdateKind.TimeLapsePhoto or FollowedUpdateKind.TankPhoto);
        if (photoTanks.Count > 0)
        {
            var rows = await PhotoUpdates(photoTanks, viewer, now, since).Where(r => ats.Contains(r.At)).ToListAsync(cancellationToken);
            var picked = new Dictionary<long, PhotoRow>();
            foreach (var winner in winners.Where(w => w.Kind is FollowedUpdateKind.TimeLapsePhoto or FollowedUpdateKind.TankPhoto))
            {
                var timeLapse = winner.Kind == FollowedUpdateKind.TimeLapsePhoto;
                var row = rows.Where(r => r.TankId == winner.TankId && r.At == winner.At && r.TimeLapse == timeLapse).OrderByDescending(r => r.LinkId).FirstOrDefault();
                if (row is not null) picked[winner.TankId] = row;
            }

            var images = await ImagesAsync(picked.Values.Select(r => r.MediaItemId).Distinct().ToList(), viewer, now, cancellationToken);
            foreach (var winner in winners.Where(w => picked.ContainsKey(w.TankId)))
                result[winner.TankId] = new FollowedUpdateDto(winner.Kind, winner.At, null, null, images[picked[winner.TankId].MediaItemId]);
        }

        return result;
    }

    private async Task<Dictionary<long, ImageUrlsDto>> ImagesAsync(
        IReadOnlyCollection<long> mediaItemIds, long viewer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Count == 0) return [];
        var variants = (await context.MediaVariants.AsNoTracking().Where(v => mediaItemIds.Contains(v.MediaItemId)).ToListAsync(cancellationToken))
            .ToLookup(v => v.MediaItemId);
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(mediaItemIds, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(mediaItemIds, viewer, now, cancellationToken);
        var builder = new MediaUrlBuilder(objectStore, signer);
        return mediaItemIds.ToDictionary(id => id, id => builder.Build(
            variants[id], allowSigned: true, anonymousVisible.Contains(id), generations.TryGetValue(id, out var generation) ? generation : null));
    }
}
