using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Persistence;

/// <summary>
/// Queries over the tanks a viewer reaches, evaluated against the follows and selected members stored in
/// the database. A null viewer is a caller without an account.
/// </summary>
public static class TankAudienceQueries
{
    /// <summary>
    /// The tanks whose profile the viewer sees, the viewer's own tanks included. Publication state and
    /// deletion are not part of the answer.
    /// </summary>
    public static IQueryable<Tank> TanksVisibleTo(this CichlidsDbContext db, long? viewerProfileId) =>
        db.Tanks.Where(TankVisibilityRules.ProfileVisibleTo(db.Follows, db.TankSelectedMembers, viewerProfileId));

    /// <summary>
    /// The tanks whose care stream the viewer sees, the viewer's own tanks included. Publication state
    /// and deletion are not part of the answer.
    /// </summary>
    public static IQueryable<Tank> TanksStreamVisibleTo(this CichlidsDbContext db, long? viewerProfileId) =>
        db.Tanks.Where(TankVisibilityRules.StreamVisibleTo(db.Follows, db.TankSelectedMembers, viewerProfileId));

    /// <summary>
    /// The relation of a viewer to one tank for the visibility rules: whether the viewer is an approved
    /// follower and whether the owner selected the viewer.
    /// </summary>
    public static async Task<(bool IsApprovedFollower, bool IsSelectedMember)> RelationToTankAsync(
        this CichlidsDbContext db, long tankId, long? viewerProfileId, CancellationToken cancellationToken)
    {
        if (viewerProfileId is not { } viewer)
        {
            return (false, false);
        }

        var follower = await db.Follows.AsNoTracking().AnyAsync(
            f => f.TargetTankId == tankId && f.FollowerProfileId == viewer && f.Status == FollowStatus.Approved, cancellationToken);
        var selected = await db.TankSelectedMembers.AsNoTracking().AnyAsync(
            s => s.TankId == tankId && s.MemberProfileId == viewer, cancellationToken);
        return (follower, selected);
    }
}
