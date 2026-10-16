using System.Linq.Expressions;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// The rules that decide who sees what of a tank. A tank has a profile level and a stream level;
/// the stream level is never wider than the profile level. The owner sees everything of their own
/// tanks. Everyone else sees the profile when its level allows them, the care stream when the
/// profile and the stream level allow them, and an entry of the stream unless the owner marked it
/// only me. The decision for a viewer lives in the CanSee methods and the query predicates; a new
/// level extends Breadth and Allows.
/// </summary>
public static class TankVisibilityRules
{
    /// <summary>
    /// The breadth of a level: a larger number reaches more viewers.
    /// </summary>
    public static int Breadth(TankVisibility level) => level switch
    {
        TankVisibility.Private => 0,
        TankVisibility.Public => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// Whether a viewer of a tank that is not their own is within the audience of a level.
    /// </summary>
    public static bool Allows(TankVisibility level, TankViewer viewer) => viewer switch
    {
        TankViewer.Owner => true,
        TankViewer.Member or TankViewer.Anonymous => level == TankVisibility.Public,
        _ => throw new ArgumentOutOfRangeException(nameof(viewer)),
    };

    /// <summary>
    /// Classifies a caller against the owner of a tank: Anonymous without a profile, Owner when the
    /// profile is the owner's and Member otherwise.
    /// </summary>
    public static TankViewer ViewerOf(long? viewerProfileId, long ownerProfileId) =>
        viewerProfileId is null ? TankViewer.Anonymous
        : viewerProfileId == ownerProfileId ? TankViewer.Owner
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
    /// Whether the viewer may follow the tank: an account that does not own the tank and a profile
    /// that allows them.
    /// </summary>
    public static bool CanFollow(TankViewer viewer, TankVisibility profile) =>
        viewer == TankViewer.Member && CanSeeProfile(viewer, profile);

    /// <summary>
    /// The tanks whose profile the viewer sees. A null viewer is a caller without an account.
    /// </summary>
    public static Expression<Func<Tank, bool>> ProfileVisibleTo(long? viewerProfileId) =>
        viewerProfileId is { } viewer
            ? t => t.ProfileVisibility == TankVisibility.Public || t.ProfileId == viewer
            : t => t.ProfileVisibility == TankVisibility.Public;

    /// <summary>
    /// The tanks whose care stream the viewer sees. A null viewer is a caller without an account.
    /// </summary>
    public static Expression<Func<Tank, bool>> StreamVisibleTo(long? viewerProfileId) =>
        viewerProfileId is { } viewer
            ? t => (t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public) || t.ProfileId == viewer
            : t => t.ProfileVisibility == TankVisibility.Public && t.StreamVisibility == TankVisibility.Public;
}
