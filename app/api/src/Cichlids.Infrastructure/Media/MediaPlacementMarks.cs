using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Media;

/// <summary>
/// Queries the media items of a context and marks media items for placement. A change that can alter
/// whether anonymous viewers see an item (a link created or removed, a tank changing its visibility, an
/// entry set to only me, a post published, withdrawn or removed, a profile suspended) marks the affected
/// items in the transaction of the change by database triggers, whichever statement makes the change; the
/// placement worker then moves their variants into the area that matches the answer.
/// </summary>
public static class MediaPlacementMarks
{
    /// <summary>
    /// The media items of a tank: those linked to the tank itself and those linked to its care entries,
    /// its water change sessions and its inhabitants.
    /// </summary>
    public static IQueryable<long> MediaItemIdsOfTank(this CichlidsDbContext db, long tankId) =>
        db.MediaLinks.Where(l =>
                l.TankId == tankId
                || (l.CareEntryId != null && db.CareEntries.Any(e => e.Id == l.CareEntryId && e.TankId == tankId))
                || (l.WaterChangeSessionId != null && db.WaterChangeSessions.Any(s => s.Id == l.WaterChangeSessionId && s.TankId == tankId))
                || (l.InhabitantId != null && db.Inhabitants.Any(i => i.Id == l.InhabitantId && i.TankId == tankId)))
            .Select(l => l.MediaItemId);

    /// <summary>
    /// The media items of a care entry: those linked to the entry and, when the entry finished a water
    /// change session, those linked to the session.
    /// </summary>
    public static IQueryable<long> MediaItemIdsOfCareEntry(this CichlidsDbContext db, long careEntryId) =>
        db.MediaLinks.Where(l =>
                l.CareEntryId == careEntryId
                || (l.WaterChangeSessionId != null
                    && db.CareEntries.Any(e => e.Id == careEntryId && e.WaterChangeSessionId == l.WaterChangeSessionId)))
            .Select(l => l.MediaItemId);

    /// <summary>
    /// The media items linked to a post.
    /// </summary>
    public static IQueryable<long> MediaItemIdsOfPost(this CichlidsDbContext db, long postId) =>
        db.MediaLinks.Where(l => l.PostId == postId).Select(l => l.MediaItemId);

    /// <summary>
    /// Marks the given media items as due for placement at once, resets their failed attempts and changes
    /// their visibility generation, which ends the validity of the signed links issued before.
    /// Returns the number of marked items.
    /// </summary>
    public static Task<int> MarkForPlacementAsync(
        this CichlidsDbContext db, IQueryable<long> mediaItemIds, CancellationToken cancellationToken = default) =>
        db.MediaItems.Where(m => mediaItemIds.Contains(m.Id)).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.VisibilityGeneration, m => m.VisibilityGeneration + 1)
                .SetProperty(m => m.PlacementPending, true)
                .SetProperty(m => m.PlacementAttempts, 0)
                .SetProperty(m => m.PlacementNextAttemptAt, (DateTimeOffset?)null),
            cancellationToken);
}
