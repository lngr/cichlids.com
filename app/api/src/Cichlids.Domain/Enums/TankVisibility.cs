namespace Cichlids.Domain.Enums;

/// <summary>
/// The audience level of one layer of a tank: its profile (header, photos, time-lapse, story) or its
/// care stream. A level is stored as lower snake_case text. The levels are ordered by breadth in
/// TankVisibilityRules: public reaches more people than followers, followers more than selected members
/// and selected members more than the owner alone.
/// </summary>
public enum TankVisibility
{
    /// <summary>
    /// Only the owner of the tank.
    /// </summary>
    Private,

    /// <summary>
    /// The members the owner names, and the owner.
    /// </summary>
    Selected,

    /// <summary>
    /// The followers the owner approved, the selected members and the owner.
    /// </summary>
    Followers,

    /// <summary>
    /// Everyone, with or without an account.
    /// </summary>
    Public,
}

/// <summary>
/// How a caller relates to a tank, ordered by the audience they belong to: without an account, as a
/// member without a relation to the tank, as an approved follower, as a member the owner selected, or as
/// the owner. A caller who follows the tank and is also selected counts as selected.
/// </summary>
public enum TankViewer
{
    Anonymous,
    Member,
    Follower,
    Selected,
    Owner,
}

/// <summary>
/// The state of a follow relation to a tank: a request the owner has not answered, or an approved
/// follower.
/// </summary>
public enum FollowStatus
{
    Pending,
    Approved,
}
