namespace Cichlids.Domain.Entities;

/// <summary>
/// A freeze that kept a period of the water change streak of a tank. The period runs from the local
/// date of one water change to the local date of the water change that closes it, which is the care
/// entry of the freeze. Year is the calendar year of the period end, the year the freeze is taken from.
/// </summary>
public class StreakFreeze
{
    public long Id { get; set; }
    public long TankId { get; set; }
    public long CareEntryId { get; set; }
    public int Year { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
