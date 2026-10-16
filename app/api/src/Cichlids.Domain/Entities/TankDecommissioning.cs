using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A time in which a tank stays in the profile of its owner but is not in use. StartedAt and EndedAt are
/// the instants the owner decommissioned and recommissioned the tank, StartDate and EndDate the local
/// calendar dates of the owner at those instants. The first day belongs to the decommissioning and the
/// end day to the operating time. A decommissioning without an end is active. The start photo shows
/// the empty tank and the end photo the filled tank; both are media items that are also attached to the
/// tank as media of the decommission and recommission sections.
/// </summary>
public class TankDecommissioning
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public DecommissionScope Scope { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateOnly StartDate { get; set; }
    public long StartMediaItemId { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateOnly? EndDate { get; set; }
    public long? EndMediaItemId { get; set; }
}
