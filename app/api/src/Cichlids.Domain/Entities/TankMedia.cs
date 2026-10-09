using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A media item attached to a section of a tank (showcase, decoration or technic), ordered
/// within that section.
/// </summary>
public class TankMedia
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long MediaItemId { get; set; }
    public TankMediaSection Section { get; set; }
    public int Sort { get; set; }
}
