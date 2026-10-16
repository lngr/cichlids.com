using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A state of the rule of a tank for one kind of care from an instant on: the rhythm in days and
/// whether the rule is switched on. The rows of a tank and kind form the history of its rule.
/// </summary>
public class CarePlanRuleChange
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public CareKind Kind { get; set; }
    public int RhythmDays { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset At { get; set; }
}
