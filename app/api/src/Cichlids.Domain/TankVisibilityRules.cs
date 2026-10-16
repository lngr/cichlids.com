using System.Linq.Expressions;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// The rules that decide who sees what of a tank. A tank has a profile level and a stream level; the
/// stream level is never wider than the profile level. The levels are ordered public, followers,
/// selected, private: a level reaches the viewers named by it and by every narrower level, so the
/// audience of followers contains the selected members, and the audience of selected contains the owner.
/// The owner sees everything of their own tanks. Everyone else sees the profile when its level allows
/// them, the care stream when the profile and the stream level allow them, and an entry of the stream
/// unless the owner marked it only me. The decision for a viewer lives in the CanSee methods and the
/// query predicates; the predicates take the follow and selected member sets of the database.
/// </summary>
public static class TankVisibilityRules
{
    /// <summary>
    /// The breadth of a level: a larger number reaches more viewers.
    /// </summary>
    public static int Breadth(TankVisibility level) => level switch
    {
        TankVisibility.Private => 0,
        TankVisibility.Selected => 1,
        TankVisibility.Followers => 2,
        TankVisibility.Public => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// Whether a viewer is within the audience of a level.
    /// </summary>
    public static bool Allows(TankVisibility level, TankViewer viewer) => viewer switch
    {
        TankViewer.Owner => true,
        TankViewer.Selected => level is TankVisibility.Public or TankVisibility.Followers or TankVisibility.Selected,
        TankViewer.Follower => level is TankVisibility.Public or TankVisibility.Followers,
        TankViewer.Member or TankViewer.Anonymous => level == TankVisibility.Public,
        _ => throw new ArgumentOutOfRangeException(nameof(viewer)),
    };

    /// <summary>
    /// Classifies a caller against a tank: Anonymous without a profile, Owner when the profile is the
    /// owner's, Selected for a selected member, Follower for an approved follower and Member otherwise.
    /// </summary>
    public static TankViewer ViewerOf(
        long? viewerProfileId, long ownerProfileId, bool isApprovedFollower = false, bool isSelectedMember = false) =>
        viewerProfileId is null ? TankViewer.Anonymous
        : viewerProfileId == ownerProfileId ? TankViewer.Owner
        : isSelectedMember ? TankViewer.Selected
        : isApprovedFollower ? TankViewer.Follower
        : TankViewer.Member;

    /// <summary>
    /// Whether the viewer sees the tank itself: its page, photos, time-lapse, videos and comments.
    /// </summary>
    public static bool CanSeeProfile(TankViewer viewer, TankVisibility profile) => Allows(profile, viewer);

    /// <summary>
    /// Whether the viewer sees the care stream of the tank: entries, measurements, series, plan,
    /// water changes and their photos and videos. The stream needs a visible profile.
    /// </summary>
    public static bool CanSeeStream(TankViewer viewer, TankVisibility profile, TankVisibility stream) =>
        viewer == TankViewer.Owner || (CanSeeProfile(viewer, profile) && Allows(stream, viewer));

    /// <summary>
    /// Whether the viewer sees one care entry: the stream must be visible to them and an entry
    /// marked only me is the owner's alone.
    /// </summary>
    public static bool CanSeeEntry(TankViewer viewer, TankVisibility profile, TankVisibility stream, bool onlyMe) =>
        viewer == TankViewer.Owner || (!onlyMe && CanSeeStream(viewer, profile, stream));

    /// <summary>
    /// Whether the stream level is within the profile level, the invariant of every tank.
    /// </summary>
    public static bool IsStreamWithinProfile(TankVisibility profile, TankVisibility stream) =>
        Breadth(stream) <= Breadth(profile);

    /// <summary>
    /// Whether the viewer may send a follow request or follow the tank: an account that does not own the
    /// tank and a profile level that takes followers (public or followers).
    /// </summary>
    public static bool CanFollow(TankViewer viewer, TankVisibility profile) =>
        viewer is TankViewer.Member or TankViewer.Follower or TankViewer.Selected
        && profile is TankVisibility.Public or TankVisibility.Followers;

    /// <summary>
    /// Whether following a tank with this profile level needs the approval of the owner.
    /// </summary>
    public static bool FollowNeedsApproval(TankVisibility profile) => profile == TankVisibility.Followers;

    /// <summary>
    /// The tanks whose profile the viewer sees. A null viewer is a caller without an account.
    /// </summary>
    public static Expression<Func<Tank, bool>> ProfileVisibleTo(
        IQueryable<Follow> follows, IQueryable<TankSelectedMember> selectedMembers, long? viewerProfileId) =>
        viewerProfileId is { } viewer
            ? t => t.ProfileId == viewer
                || t.ProfileVisibility == TankVisibility.Public
                || (t.ProfileVisibility == TankVisibility.Followers
                    && (follows.Any(f => f.TargetTankId == t.Id && f.FollowerProfileId == viewer && f.Status == FollowStatus.Approved)
                        || selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer)))
                || (t.ProfileVisibility == TankVisibility.Selected
                    && selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer))
            : t => t.ProfileVisibility == TankVisibility.Public;

    /// <summary>
    /// The tanks whose care stream the viewer sees: the viewer is in the audience of the profile level
    /// and of the stream level. A null viewer is a caller without an account.
    /// </summary>
    public static Expression<Func<Tank, bool>> StreamVisibleTo(
        IQueryable<Follow> follows, IQueryable<TankSelectedMember> selectedMembers, long? viewerProfileId) =>
        viewerProfileId is { } viewer
            ? t => t.ProfileId == viewer
                || ((t.ProfileVisibility == TankVisibility.Public
                        || (t.ProfileVisibility == TankVisibility.Followers
                            && (follows.Any(f => f.TargetTankId == t.Id && f.FollowerProfileId == viewer && f.Status == FollowStatus.Approved)
                                || selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer)))
                        || (t.ProfileVisibility == TankVisibility.Selected
                            && selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer)))
                    && (t.StreamVisibility == TankVisibility.Public
                        || (t.StreamVisibility == TankVisibility.Followers
                            && (follows.Any(f => f.TargetTankId == t.Id && f.FollowerProfileId == viewer && f.Status == FollowStatus.Approved)
                                || selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer)))
                        || (t.StreamVisibility == TankVisibility.Selected
                            && selectedMembers.Any(s => s.TankId == t.Id && s.MemberProfileId == viewer))))
            : t => t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public;
}
