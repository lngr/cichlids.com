namespace Cichlids.Domain.Enums;

/// <summary>
/// The audience level of one layer of a tank: its profile (header, photos, time-lapse, story) or its
/// care stream. A level is stored as lower snake_case text. The levels are ordered by breadth in
/// TankVisibilityRules.
/// </summary>
public enum TankVisibility
{
    /// <summary>
    /// Only the owner of the tank.
    /// </summary>
    Private,

    /// <summary>
    /// Everyone, with or without an account.
    /// </summary>
    Public,
}

/// <summary>
/// How a caller relates to a tank: without an account, as a member who does not own the tank, or
/// as its owner.
/// </summary>
public enum TankViewer
{
    Anonymous,
    Member,
    Owner,
}
