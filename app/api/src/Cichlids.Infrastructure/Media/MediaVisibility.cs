using System.Linq.Expressions;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// The one rule that decides who sees a media item: a viewer sees the item when at least one of its
/// links is visible to them, and the owner always sees the own files. A file without links is visible
/// to its owner only. The visibility of a link follows from its context:
/// a tank link from the profile level of a published tank, the proof photos of a decommissioning
/// (decommission and recommission roles) from the history level of the tank, a care entry link from the
/// history level of the tank and the only me mark of the entry, a water change session link like the
/// care entry that finishes the session (an open session is the owner's alone), an inhabitant link from
/// the profile level of its tank, a post link from the state of the post (published: everyone, draft: its
/// author, archived or deleted: nobody), a discussion post link always and a profile link while the
/// profile is not suspended. The owner of a tank sees every link of the tank. A viewer without a profile
/// is anonymous; the files anonymous viewers see belong in the public storage area.
/// </summary>
public static class MediaVisibility
{
    /// <summary>
    /// The media items among the given ids that the viewer sees. A null viewer is anonymous. One query
    /// answers for all ids, so lists stay fast; the ids are matched through the index of the link table.
    /// </summary>
    public static async Task<HashSet<long>> VisibleMediaItemIdsAsync(
        this CichlidsDbContext db, IReadOnlyCollection<long> mediaItemIds, long? viewerProfileId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (mediaItemIds.Count == 0)
        {
            return [];
        }

        var ids = await db.VisibleMediaItems(viewerProfileId, now).AsNoTracking()
            .Where(m => mediaItemIds.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken);
        return [.. ids];
    }

    /// <summary>
    /// The visibility generation of each media item among the given ids that the viewer sees. The visibility
    /// check and the read of the generation are one statement, so a generation is never newer than the state
    /// the check saw. Callers issue a signed link only from this generation.
    /// </summary>
    public static async Task<Dictionary<long, long>> VisibleMediaGenerationsAsync(
        this CichlidsDbContext db, IReadOnlyCollection<long> mediaItemIds, long? viewerProfileId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (mediaItemIds.Count == 0)
        {
            return [];
        }

        return await db.VisibleMediaItems(viewerProfileId, now).AsNoTracking()
            .Where(m => mediaItemIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.VisibilityGeneration, cancellationToken);
    }

    /// <summary>
    /// The media items among the given ids that anonymous viewers see, which are the items that belong in
    /// the public storage area.
    /// </summary>
    public static Task<HashSet<long>> AnonymousVisibleMediaItemIdsAsync(
        this CichlidsDbContext db, IReadOnlyCollection<long> mediaItemIds, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        db.VisibleMediaItemIdsAsync(mediaItemIds, null, now, cancellationToken);

    /// <summary>
    /// All media items the viewer sees, as a query to compose further conditions onto. A null viewer is
    /// anonymous.
    /// </summary>
    public static IQueryable<MediaItem> VisibleMediaItems(this CichlidsDbContext db, long? viewerProfileId, DateTimeOffset now)
    {
        var linkVisible = LinkVisibleTo(db, viewerProfileId, now);
        return db.MediaItems.Where(m =>
            (viewerProfileId != null && m.OwnerProfileId == viewerProfileId)
            || db.MediaLinks.Where(linkVisible).Any(l => l.MediaItemId == m.Id));
    }

    /// <summary>
    /// The media items that anonymous viewers see, which are the items that belong in the public storage
    /// area.
    /// </summary>
    public static IQueryable<MediaItem> AnonymousVisibleMediaItems(this CichlidsDbContext db, DateTimeOffset now) =>
        db.VisibleMediaItems(null, now);

    /// <summary>
    /// The links the viewer sees, as a predicate over the link table.
    /// </summary>
    public static Expression<Func<MediaLink, bool>> LinkVisibleTo(CichlidsDbContext db, long? viewerProfileId, DateTimeOffset now)
    {
        var viewer = viewerProfileId;
        return l =>
            (l.ContextType == MediaLinkContext.Tank
                && ((l.Role == MediaLinkRole.Decommission || l.Role == MediaLinkRole.Recommission)
                    ? db.Tanks.Any(t => t.Id == l.TankId
                        && ((t.State == TankState.Published && t.DeletedAt == null
                            && t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public)
                            || t.ProfileId == viewer))
                    : db.Tanks.Any(t => t.Id == l.TankId
                        && ((t.State == TankState.Published && t.DeletedAt == null && t.ProfileVisibility == TankVisibility.Public)
                            || t.ProfileId == viewer))))
            || (l.ContextType == MediaLinkContext.Inhabitant
                && db.Inhabitants.Any(i => i.Id == l.InhabitantId && db.Tanks.Any(t => t.Id == i.TankId
                    && ((t.State == TankState.Published && t.DeletedAt == null && t.ProfileVisibility == TankVisibility.Public)
                        || t.ProfileId == viewer))))
            || (l.ContextType == MediaLinkContext.CareEntry
                && db.CareEntries.Any(e => e.Id == l.CareEntryId && db.Tanks.Any(t => t.Id == e.TankId
                    && ((!e.OnlyMe && t.State == TankState.Published && t.DeletedAt == null
                        && t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public)
                        || t.ProfileId == viewer))))
            || (l.ContextType == MediaLinkContext.WaterChangeSession
                && db.WaterChangeSessions.Any(s => s.Id == l.WaterChangeSessionId && db.Tanks.Any(t => t.Id == s.TankId
                    && ((db.CareEntries.Any(e => e.WaterChangeSessionId == s.Id && !e.OnlyMe)
                        && t.State == TankState.Published && t.DeletedAt == null
                        && t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public)
                        || t.ProfileId == viewer))))
            || (l.ContextType == MediaLinkContext.Post
                && db.Posts.Any(p => p.Id == l.PostId && p.DeletedAt == null
                    && (p.State == PostState.Published || (p.State == PostState.Draft && p.AuthorProfileId == viewer))))
            || l.ContextType == MediaLinkContext.DiscussionPost
            || (l.ContextType == MediaLinkContext.Profile
                && (l.ProfileId == viewer
                    || db.Profiles.Any(p => p.Id == l.ProfileId && (p.SuspendedUntil == null || p.SuspendedUntil <= now))));
    }
}
