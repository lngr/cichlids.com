using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A media item attached to a section of a tank (showcase, decoration, technic, the time-lapse series or the videos), ordered
/// within that section.
/// </summary>
public class TankMedia
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long MediaItemId { get; set; }
    public TankMediaSection Section { get; set; }
    public int Sort { get; set; }

    /// <summary>
    /// Whether the photo was taken with the camera of the app. Only such photos of the time-lapse
    /// section count for the time-lapse streak.
    /// </summary>
    public bool CapturedInApp { get; set; }
}
