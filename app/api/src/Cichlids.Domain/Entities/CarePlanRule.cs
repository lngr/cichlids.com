using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// The stored rule of a tank for one kind of care: how often the care is due, whether the rule is
/// switched on, and the reason code with its optional parameters (a JSON object of strings).
/// </summary>
public class CarePlanRule
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public CareKind Kind { get; set; }
    public int RhythmDays { get; set; }
    public bool Enabled { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? ReasonParametersJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
