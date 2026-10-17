using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A stocking entry of a tank: a group (species and count) or a named fish (a name and a count of one,
/// with an optional species). Moved-in day and removal day are calendar dates in the owner's local time;
/// a row without a moved-in day was migrated and counts as present since the tank was created. A removed
/// inhabitant stays as history of the tank. The owner is the member the inhabitant belongs to, which is
/// the owner of the tank it lives in.
/// </summary>
public class Inhabitant
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long OwnerProfileId { get; set; }
    public long? SpeciesId { get; set; }
    public int? Count { get; set; }
    public string? Name { get; set; }
    public DateOnly? MovedInOn { get; set; }
    public DateOnly? RemovedOn { get; set; }
    public InhabitantRemovalReason? RemovalReason { get; set; }
    public int Sort { get; set; }
}
